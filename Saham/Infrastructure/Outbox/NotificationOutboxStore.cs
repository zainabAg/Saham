using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Saham.Infrastructure.Outbox;

/* Phase 6 store over the existing notification queue (AppNotifications — one row = one channel +
 * one recipient, unique per (EventKey, Channel, UserId) with NULLS NOT DISTINCT). Same algorithm as the
 * package's PostgresOutboxStore, adapted to this table:
 *   - claim ONE due row with FOR UPDATE SKIP LOCKED and a lease in database time; the attempt counts
 *     at claim, so a worker that dies also uses an attempt;
 *   - ack (Sent / Failed / Dead) only while this claim still owns an unexpired lease;
 *   - an expired lease is claimed again; at the attempt limit it becomes Dead instead.
 * Every call opens its own connection (autocommit statements): no transaction is held while the
 * provider is called, and the request/UoW connection is never used. Times are UTC "timestamp without
 * time zone" like the rest of the schema, hence clock_timestamp() AT TIME ZONE 'UTC'. */
public sealed class NotificationOutboxStore : IOutboxStore
{
    public const string AggregateType = "Notification";
    private const string DbNow = "(clock_timestamp() AT TIME ZONE 'UTC')";

    private readonly Func<CancellationToken, Task<DbConnection>> _openConnection;
    private readonly OutboxOptions _options;
    private readonly Func<IReadOnlyList<DeadNotification>, Task>? _onDead;

    public NotificationOutboxStore(Func<CancellationToken, Task<DbConnection>> openConnection, OutboxOptions options,
        Func<IReadOnlyList<DeadNotification>, Task>? onDead = null)
    {
        _openConnection = openConnection ?? throw new ArgumentNullException(nameof(openConnection));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _onDead = onDead;
    }

    public async Task<OutboxClaim?> ClaimAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _openConnection(cancellationToken);

        // Exhausted rows (their last attempt failed while the worker died) are kept as Dead for review.
        await using (var recovery = connection.CreateCommand())
        {
            recovery.CommandText = $"""
                WITH exhausted AS (
                    SELECT "Id" FROM "AppNotifications"
                    WHERE "Attempts" >= @max_attempts
                      AND ("Status" IN ('Queued', 'Failed')
                        OR ("Status" = 'Processing' AND "LeaseUntil" <= {DbNow}))
                    ORDER BY "Id" LIMIT 100 FOR UPDATE SKIP LOCKED)
                UPDATE "AppNotifications" AS n
                SET "Status" = 'Dead', "LeaseToken" = NULL, "LeaseUntil" = NULL,
                    "LastError" = 'ATTEMPT_LIMIT_EXHAUSTED', "ConcurrencyStamp" = @stamp
                FROM exhausted AS x WHERE n."Id" = x."Id"
                RETURNING n."Id", n."Template", n."OrderId";
                """;
            Add(recovery, "max_attempts", _options.MaxAttempts, DbType.Int32);
            Add(recovery, "stamp", NewStamp(), DbType.String);
            await ReportDeadAsync(recovery, cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            WITH candidate AS (
                SELECT "Id" FROM "AppNotifications"
                WHERE "Attempts" < @max_attempts
                  AND (("Status" IN ('Queued', 'Failed') AND "NextAttemptAt" <= {DbNow})
                    OR ("Status" = 'Processing' AND "LeaseUntil" <= {DbNow}))
                ORDER BY "HighPriority" DESC, "NextAttemptAt", "Id"
                LIMIT 1 FOR UPDATE SKIP LOCKED)
            UPDATE "AppNotifications" AS n
            SET "Status" = 'Processing', "Attempts" = n."Attempts" + 1,
                "LeaseToken" = @token, "LeaseUntil" = {DbNow} + @lease_ms * INTERVAL '1 millisecond',
                "ConcurrencyStamp" = @stamp
            FROM candidate AS c WHERE n."Id" = c."Id"
            RETURNING n."Id", n."Attempts", n."EventKey", n."Template";
            """;
        var token = Guid.NewGuid();
        Add(command, "token", token, DbType.Guid);
        Add(command, "max_attempts", _options.MaxAttempts, DbType.Int32);
        Add(command, "lease_ms", _options.LeaseDuration.TotalMilliseconds, DbType.Double);
        Add(command, "stamp", NewStamp(), DbType.String);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        // Message.EventKey = the notification id: stable for this channel + recipient, so it is the
        // provider idempotency key (one event can have several notifications, each with its own id).
        var id = reader.GetGuid(0);
        return new OutboxClaim(0, token, reader.GetInt32(1),
            new OutboxMessage(id, reader.GetString(3), AggregateType, id.ToString(), "{}"));
    }

    public Task<bool> CompleteAsync(OutboxClaim claim, string? providerMessageId, CancellationToken cancellationToken) =>
        UpdateOwnedAsync(claim, $"""
            UPDATE "AppNotifications"
            SET "Status" = 'Sent', "SentAt" = {DbNow}, "ProviderMessageId" = @provider_id,
                "LeaseToken" = NULL, "LeaseUntil" = NULL, "LastError" = NULL, "ConcurrencyStamp" = @stamp
            WHERE "Id" = @id AND "Status" = 'Processing' AND "LeaseToken" = @token AND "LeaseUntil" > {DbNow}
            RETURNING "Id", "Template", "OrderId", "Status";
            """, command => Add(command, "provider_id", (object?)Truncate(providerMessageId, 128) ?? DBNull.Value, DbType.String),
            cancellationToken);

    public Task<bool> FailAsync(OutboxClaim claim, bool permanent, TimeSpan delay, string safeErrorCode, CancellationToken cancellationToken)
    {
        if (delay < TimeSpan.Zero || delay > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(delay));
        }

        // The persisted attempt count decides the last attempt, not a value from the caller.
        return UpdateOwnedAsync(claim, $"""
            UPDATE "AppNotifications"
            SET "Status" = CASE WHEN @permanent OR "Attempts" >= @max_attempts THEN 'Dead' ELSE 'Failed' END,
                "NextAttemptAt" = {DbNow} + @delay_ms * INTERVAL '1 millisecond',
                "LeaseToken" = NULL, "LeaseUntil" = NULL, "LastError" = @error, "ConcurrencyStamp" = @stamp
            WHERE "Id" = @id AND "Status" = 'Processing' AND "LeaseToken" = @token AND "LeaseUntil" > {DbNow}
            RETURNING "Id", "Template", "OrderId", "Status";
            """, command =>
            {
                Add(command, "permanent", permanent, DbType.Boolean);
                Add(command, "max_attempts", _options.MaxAttempts, DbType.Int32);
                Add(command, "delay_ms", delay.TotalMilliseconds, DbType.Double);
                Add(command, "error", Truncate(safeErrorCode, 120)!, DbType.String);
            }, cancellationToken);
    }

    /* False = the lease was lost (expired or claimed again): an old worker never updates a newer claim. */
    private async Task<bool> UpdateOwnedAsync(OutboxClaim claim, string sql, Action<DbCommand> configure, CancellationToken cancellationToken)
    {
        await using var connection = await _openConnection(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        Add(command, "id", claim.Message.EventKey, DbType.Guid);
        Add(command, "token", claim.LeaseToken, DbType.Guid);
        Add(command, "stamp", NewStamp(), DbType.String);
        configure(command);
        return await ReportDeadAsync(command, cancellationToken) > 0;
    }

    /* Runs an UPDATE … RETURNING "Id", "Template", "OrderId"[, "Status"]; Dead rows go to the callback. */
    private async Task<int> ReportDeadAsync(DbCommand command, CancellationToken cancellationToken)
    {
        var dead = new List<DeadNotification>();
        var rows = 0;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows++;
                if (reader.FieldCount < 4 || reader.GetString(3) == "Dead")
                {
                    dead.Add(new DeadNotification(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetGuid(2)));
                }
            }
        }

        if (dead.Count > 0 && _onDead != null)
        {
            await _onDead(dead);
        }

        return rows;
    }

    private static void Add(DbCommand command, string name, object value, DbType type)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static string NewStamp() => Guid.NewGuid().ToString("N");

    private static string? Truncate(string? value, int max) => value == null || value.Length <= max ? value : value[..max];
}

public sealed record DeadNotification(Guid Id, string Template, Guid? OrderId);
