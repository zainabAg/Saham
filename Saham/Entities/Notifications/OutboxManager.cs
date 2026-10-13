using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Saham.Entities.Notifications;

/* Writes business events to the outbox, inside the caller's transaction (SRS I-6) — the SAME
 * connection and transaction as the business change (Phase 6): a rollback removes both, and the
 * relay only ever sees committed events. A failure here fails the business operation.
 * The key is stable for the logical change, so a retry of the same change reuses the row; the same
 * key with other content is refused (OutboxEventKeyConflict) instead of silently counting as sent. */
public class OutboxManager : DomainService
{
    private readonly IRepository<OutboxEvent, long> _outboxRepository;

    public OutboxManager(IRepository<OutboxEvent, long> outboxRepository)
    {
        _outboxRepository = outboxRepository;
    }

    /* The key is "{type}:{aggregate}" for events that happen once per aggregate (ORDER_PICKED_UP);
     * events that may repeat (a second driver assigned, an item running out twice) pass a discriminator. */
    public async Task EnqueueAsync(string eventType, string aggregateType, Guid aggregateId,
        IReadOnlyDictionary<string, string?> payload, string? discriminator = null)
    {
        var key = discriminator == null ? $"{eventType}:{aggregateId:N}" : $"{eventType}:{aggregateId:N}:{discriminator}";
        var database = (await _outboxRepository.GetDbContextAsync()).Database;
        var transaction = database.CurrentTransaction?.GetDbTransaction()
                          ?? throw new AbpException("Outbox events need the business change's transaction.");
        if (await UpsertAsync(database.GetDbConnection(), transaction, eventType, key, aggregateType, aggregateId,
                JsonSerializer.Serialize(payload), Clock.Now) == null)
        {
            throw new BusinessException(SahamErrorCodes.OutboxEventKeyConflict).WithData("eventKey", key);
        }
    }

    /* INSERT or, for the same key, keep the existing row only when type, aggregate and payload (jsonb
     * equality) match; otherwise no row is returned. A concurrent writer of the same key waits for the
     * first to commit, then compares. Public for the PostgreSQL integration tests. */
    public static async Task<long?> UpsertAsync(DbConnection connection, DbTransaction transaction, string eventType, string eventKey,
        string aggregateType, Guid aggregateId, string payloadJson, DateTime now)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO "AppOutboxEvents" ("EventType", "EventKey", "AggregateType", "AggregateId", "Payload", "CreationTime", "Attempts")
            VALUES (@type, @key, @aggregate_type, @aggregate_id, CAST(@payload AS jsonb), @now, 0)
            ON CONFLICT ("EventKey") DO UPDATE SET "EventKey" = EXCLUDED."EventKey"
            WHERE "AppOutboxEvents"."EventType" = EXCLUDED."EventType"
              AND "AppOutboxEvents"."AggregateType" = EXCLUDED."AggregateType"
              AND "AppOutboxEvents"."AggregateId" = EXCLUDED."AggregateId"
              AND "AppOutboxEvents"."Payload" = EXCLUDED."Payload"
            RETURNING "Id";
            """;
        Add(command, "type", eventType, DbType.String);
        Add(command, "key", eventKey, DbType.String);
        Add(command, "aggregate_type", aggregateType, DbType.String);
        Add(command, "aggregate_id", aggregateId, DbType.Guid);
        Add(command, "payload", payloadJson, DbType.String);
        Add(command, "now", now, DbType.DateTime);
        var id = await command.ExecuteScalarAsync();
        return id is null or DBNull ? null : Convert.ToInt64(id);
    }

    private static void Add(DbCommand command, string name, object value, DbType type)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
