using System.Data.Common;
using System.Text.Json;
using Npgsql;
using Saham.Entities.Notifications;
using Saham.Infrastructure.Outbox;

// Phase 6 INTEGRATION_CHECKLIST against a REAL PostgreSQL test database with independent connections.
// Usage: dotnet run --project test/Saham.Outbox.PostgresChecks -- <secrets json whose Default points at saham_test>
// Refuses any database not named saham_test. Run with the backend STOPPED (its worker would claim test rows).
// Before the checks, every not-final notification in the test DB is set Dead ("TEST_RESET") so the store
// only sees this run's rows.

var secrets = JsonDocument.Parse(File.ReadAllText(args[0]));
var cs = new NpgsqlConnectionStringBuilder(secrets.RootElement.GetProperty("ConnectionStrings").GetProperty("Default").GetString());
if (cs.Database != "saham_test") throw new InvalidOperationException("Refusing to run outside saham_test.");
var connectionString = cs.ConnectionString;
int passed = 0, failed = 0;
var run = Guid.NewGuid().ToString("N")[..8];

void Check(bool ok, string name, object? detail = null)
{
    if (ok) passed++; else failed++;
    Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (detail == null ? "" : "  -> " + detail));
}

async Task<NpgsqlConnection> Open(CancellationToken ct = default)
{
    var c = new NpgsqlConnection(connectionString);
    await c.OpenAsync(ct);
    return c;
}

async Task<object?> Scalar(string sql, params (string, object)[] ps)
{
    await using var c = await Open();
    await using var cmd = new NpgsqlCommand(sql, c);
    foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
    return await cmd.ExecuteScalarAsync();
}

async Task Exec(string sql, params (string, object)[] ps) => await Scalar(sql, ps);

async Task<Guid> NewNotification(string tag, string channel = "Push", Guid? user = null, DateTime? nextAttempt = null, string? eventKey = null)
{
    var id = Guid.NewGuid();
    await Exec("""
        INSERT INTO "AppNotifications" ("Id","EventKey","Channel","UserId","Phone","Template","Payload","HighPriority","Status","Attempts",
            "NextAttemptAt","ExtraProperties","ConcurrencyStamp","CreationTime")
        VALUES (@id,@key,@ch,@user,@phone,@tpl,'{"Args":[]}'::jsonb,false,'Queued',0,
            COALESCE(@next,(clock_timestamp() AT TIME ZONE 'UTC') - interval '1 second'),'{}',@stamp,(now() AT TIME ZONE 'UTC'))
        """, ("id", id), ("key", eventKey ?? $"TEST:{run}:{tag}:{id:N}"), ("ch", channel), ("user", (object?)user ?? Guid.NewGuid()),
        ("phone", channel == "Sms" ? "+9647800000000" : (object)DBNull.Value), ("tpl", $"Test:{run}:{tag}"),
        ("next", (object?)nextAttempt ?? DBNull.Value), ("stamp", Guid.NewGuid().ToString("N")));
    return id;
}

async Task<(string Status, int Attempts, Guid? Token, string? Error, DateTime? SentAt, DateTime Next)> Row(Guid id)
{
    await using var c = await Open();
    await using var cmd = new NpgsqlCommand("""SELECT "Status","Attempts","LeaseToken","LastError","SentAt","NextAttemptAt" FROM "AppNotifications" WHERE "Id"=@id""", c);
    cmd.Parameters.AddWithValue("id", id);
    await using var r = await cmd.ExecuteReaderAsync();
    await r.ReadAsync();
    return (r.GetString(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetGuid(2), r.IsDBNull(3) ? null : r.GetString(3),
        r.IsDBNull(4) ? null : r.GetDateTime(4), r.GetDateTime(5));
}

async Task<DateTime> DbNow() => (DateTime)(await Scalar("SELECT (clock_timestamp() AT TIME ZONE 'UTC')"))!;

var options = new OutboxOptions { MaxAttempts = 3, SendTimeout = TimeSpan.FromSeconds(1), LeaseDuration = TimeSpan.FromSeconds(12),
    InitialRetryDelay = TimeSpan.FromSeconds(2), MaximumRetryDelay = TimeSpan.FromSeconds(30) };
var dead = new List<DeadNotification>();
NotificationOutboxStore Store() => new(async ct => (DbConnection)await Open(ct), options, d => { lock (dead) dead.AddRange(d); return Task.CompletedTask; });

async Task<OutboxClaim?> ClaimOnly(Guid id)
{
    // Claims until it gets the wanted row (other rows of this run may be due as well) — bounded.
    for (var i = 0; i < 50; i++)
    {
        var c = await Store().ClaimAsync(CancellationToken.None);
        if (c == null) return null;
        if (c.Message.EventKey == id) return c;
        await Store().FailAsync(c, false, TimeSpan.FromHours(1), "TEST_PARKED", CancellationToken.None);
    }
    return null;
}

await Exec("""UPDATE "AppNotifications" SET "Status"='Dead',"LastError"='TEST_RESET',"LeaseToken"=NULL,"LeaseUntil"=NULL WHERE "Status" IN ('Queued','Failed','Processing')""");

// ---------------------------------------------------------------- 1–4, 17: the event with the business change
Console.WriteLine("\n== 1–4, 17. event in the business transaction ==");
var evKey = $"TEST_EVENT:{run}";
await using (var c = await Open())
await using (var tx = await c.BeginTransactionAsync())
{
    await OutboxManager.UpsertAsync(c, tx, "TEST_EVENT", evKey + ":rb", "Order", Guid.NewGuid(), """{"a":"1"}""", DateTime.UtcNow);
    await tx.RollbackAsync();
}
Check((long)(await Scalar("""SELECT COUNT(*) FROM "AppOutboxEvents" WHERE "EventKey"=@k""", ("k", evKey + ":rb")))! == 0, "1. rollback: no event remains");

var aggregate = Guid.NewGuid();
await using (var c = await Open())
await using (var tx = await c.BeginTransactionAsync())
{
    var id = await OutboxManager.UpsertAsync(c, tx, "TEST_EVENT", evKey, "Order", aggregate, """{"a":"1","b":"2"}""", DateTime.UtcNow);
    var visible = await Scalar("""SELECT COUNT(*) FROM "AppOutboxEvents" WHERE "EventKey"=@k AND "PublishedAt" IS NULL""", ("k", evKey));
    Check(id != null && (long)visible! == 0, "2. before commit the relay cannot see the event");
    await tx.CommitAsync();
}
Check((long)(await Scalar("""SELECT COUNT(*) FROM "AppOutboxEvents" WHERE "EventKey"=@k""", ("k", evKey)))! == 1, "2. after commit: the event is there");

long? same1, same2;
await using (var c = await Open())
await using (var tx = await c.BeginTransactionAsync())
{
    same1 = await OutboxManager.UpsertAsync(c, tx, "TEST_EVENT", evKey, "Order", aggregate, """{ "b":"2", "a":"1" }""", DateTime.UtcNow);
    await tx.CommitAsync();
}
Check(same1 != null && (long)(await Scalar("""SELECT COUNT(*) FROM "AppOutboxEvents" WHERE "EventKey"=@k""", ("k", evKey)))! == 1,
    "3. same key + same jsonb (other order/spacing): same id, one row", same1);
await using (var c = await Open())
await using (var tx = await c.BeginTransactionAsync())
{
    same2 = await OutboxManager.UpsertAsync(c, tx, "TEST_EVENT", evKey, "Order", aggregate, """{"a":"1","b":"3"}""", DateTime.UtcNow);
    await tx.RollbackAsync();
}
Check(same2 == null, "4. same key + different payload: refused (no row returned)");

// 17: two connections write the same change at once
var key17 = $"TEST_EVENT:{run}:17";
var gate = new TaskCompletionSource();
async Task<long?> Writer()
{
    await using var c = await Open();
    await using var tx = await c.BeginTransactionAsync();
    await gate.Task;
    var id = await OutboxManager.UpsertAsync(c, tx, "TEST_EVENT", key17, "Order", aggregate, """{"x":"1"}""", DateTime.UtcNow);
    await tx.CommitAsync();
    return id;
}
var w = new[] { Writer(), Writer() };
gate.SetResult();
var ids17 = await Task.WhenAll(w);
Check(ids17[0] != null && ids17[0] == ids17[1] && (long)(await Scalar("""SELECT COUNT(*) FROM "AppOutboxEvents" WHERE "EventKey"=@k""", ("k", key17)))! == 1,
    "17. two concurrent writers of the same change: one row, same id", string.Join(",", ids17));

// ---------------------------------------------------------------- 5–6: competing workers
Console.WriteLine("\n== 5–6. competing workers ==");
var batch = new List<Guid>();
for (var i = 0; i < 10; i++) batch.Add(await NewNotification("c5"));
var claims = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => Store().ClaimAsync(CancellationToken.None))));
var got = claims.Where(c => c != null).ToList();
Check(got.Count == 10 && got.Select(c => c!.Message.EventKey).Distinct().Count() == 10 && got.All(c => batch.Contains(c!.Message.EventKey)),
    "5. 20 workers, 10 messages: 10 claims, each row exactly once", got.Count);
var tokens = new HashSet<Guid>();
foreach (var c in got) tokens.Add((await Row(c!.Message.EventKey)).Token!.Value);
Check(tokens.Count == 10 && got.All(c => tokens.Contains(c!.LeaseToken)), "5. every lease belongs to one worker (token per row)");
foreach (var c in got) await Store().CompleteAsync(c!, null, CancellationToken.None);

var locked = await NewNotification("c6", nextAttempt: DateTime.UtcNow.AddMinutes(-2));
var other = await NewNotification("c6", nextAttempt: DateTime.UtcNow.AddMinutes(-1));
await using (var holder = await Open())
await using (var htx = await holder.BeginTransactionAsync())
{
    await using (var lk = new NpgsqlCommand("""SELECT 1 FROM "AppNotifications" WHERE "Id"=@id FOR UPDATE""", holder, htx))
    {
        lk.Parameters.AddWithValue("id", locked);
        await lk.ExecuteScalarAsync();
    }
    var c6 = await Store().ClaimAsync(CancellationToken.None);
    Check(c6 != null && c6.Message.EventKey == other, "6. a row locked by another transaction is skipped, the next is claimed");
    if (c6 != null) await Store().CompleteAsync(c6, null, CancellationToken.None);
    await htx.RollbackAsync();
}
var c6b = await ClaimOnly(locked);
if (c6b != null) await Store().CompleteAsync(c6b, null, CancellationToken.None);

// ---------------------------------------------------------------- 7–9: crash, stale worker, expired lease
Console.WriteLine("\n== 7–9. crash after claim, stale ack, expired lease ==");
var n7 = await NewNotification("c7");
var first = await ClaimOnly(n7);
Check(first != null && (await Row(n7)).Status == "Processing", "7. claimed (worker 'crashes' now, no ack)");
Check(await Store().ClaimAsync(CancellationToken.None) == null, "7. while the lease runs, nobody else gets it");
var ok9 = false;
await Task.Delay(options.LeaseDuration + TimeSpan.FromSeconds(1));
ok9 = !await Store().CompleteAsync(first!, null, CancellationToken.None);
Check(ok9 && (await Row(n7)).Status == "Processing", "9. ack after the lease expired (before any reclaim) is refused");
var second = await ClaimOnly(n7);
var r7 = await Row(n7);
Check(second != null && r7.Attempts == 2 && r7.Token == second.LeaseToken, "7. after expiry it is claimed again, attempt 2", r7.Attempts);
Check(!await Store().CompleteAsync(first!, null, CancellationToken.None) && !await Store().FailAsync(first!, true, TimeSpan.Zero, "STALE", CancellationToken.None)
      && (await Row(n7)).Token == second!.LeaseToken, "8. the old worker's Complete/Fail return false and leave the new lease");
await Store().CompleteAsync(second!, null, CancellationToken.None);

// ---------------------------------------------------------------- 10: provider accepted, ack lost
Console.WriteLine("\n== 10. provider accepted, then the ack was lost ==");
var n10 = await NewNotification("c10", "Sms");
async Task ProviderAccept(Guid key)
{
    // The SmsLogs unique IdempotencyKey index is what LoggingSmsSender relies on (provider-side dedupe).
    await Exec("""
        INSERT INTO "AppSmsLogs" ("Id","PhoneNumber","Text","Status","Provider","IdempotencyKey","ExtraProperties","ConcurrencyStamp","CreationTime")
        VALUES (@id,'+9647800000000','test','Sent','log',@key,'{}',@stamp,(now() AT TIME ZONE 'UTC')) ON CONFLICT DO NOTHING
        """, ("id", Guid.NewGuid()), ("key", key.ToString("N")), ("stamp", Guid.NewGuid().ToString("N")));
}
var a10 = await ClaimOnly(n10);
await ProviderAccept(n10); // accepted … and the worker dies before CompleteAsync
await Task.Delay(options.LeaseDuration + TimeSpan.FromSeconds(1));
var b10 = await ClaimOnly(n10);
await ProviderAccept(n10); // resent with the SAME key
var acked = await Store().CompleteAsync(b10!, null, CancellationToken.None);
var logs = (long)(await Scalar("""SELECT COUNT(*) FROM "AppSmsLogs" WHERE "IdempotencyKey"=@k""", ("k", n10.ToString("N"))))!;
Check(a10 != null && b10 != null && acked && logs == 1 && (await Row(n10)).Status == "Sent",
    "10. resent with the same key; the provider kept one message (at-least-once to the provider, deduped there)", logs);

// ---------------------------------------------------------------- 11–16: timing, DEAD, cancel, permanent, Sent
Console.WriteLine("\n== 11–16. due time, attempts, DEAD, shutdown, permanent, Sent ==");
var n11 = await NewNotification("c11", nextAttempt: (await DbNow()).AddMinutes(5));
Check(await ClaimOnly(n11) == null, "11. NextAttemptAt in the future (database time): not claimed");
await Exec("""UPDATE "AppNotifications" SET "Status"='Dead',"LastError"='TEST_DONE' WHERE "Id"=@id""", ("id", n11));

var n12 = await NewNotification("c12");
for (var i = 1; i <= options.MaxAttempts; i++)
{
    var c = await ClaimOnly(n12);
    if (c == null) break;
    await Store().FailAsync(c, false, TimeSpan.Zero, "PROVIDER_RETRYABLE_FAILURE", CancellationToken.None);
}
var r12 = await Row(n12);
Check(r12.Status == "Dead" && r12.Attempts == options.MaxAttempts && await ClaimOnly(n12) == null, "12. last attempt fails: DEAD, never claimed again", r12);

var n13 = await NewNotification("c13");
await Exec("""UPDATE "AppNotifications" SET "Attempts"=@a WHERE "Id"=@id""", ("a", options.MaxAttempts - 1), ("id", n13));
var c13 = await ClaimOnly(n13); // the last attempt … and the worker dies
await Task.Delay(options.LeaseDuration + TimeSpan.FromSeconds(1));
await Store().ClaimAsync(CancellationToken.None); // recovery runs at every claim
var r13 = await Row(n13);
Check(c13 != null && r13.Status == "Dead" && r13.Error == "ATTEMPT_LIMIT_EXHAUSTED" && r13.Token == null,
    "13. last attempt crashed: recovered as DEAD (ATTEMPT_LIMIT_EXHAUSTED)", r13);

var n14 = await NewNotification("c14");
using (var stop = new CancellationTokenSource())
{
    var sender = new BlockingSender(n14, stop);
    var threw = false;
    try { await new OutboxProcessor(new OnlyStore(Store(), n14), sender, options).RunOnceAsync(5, stop.Token); }
    catch (OperationCanceledException) { threw = true; }
    var r14 = await Row(n14);
    Check(threw && r14.Status == "Processing" && r14.SentAt == null, "14. shutdown during the send: nothing recorded, lease kept for recovery", r14.Status);
}
await Task.Delay(options.LeaseDuration + TimeSpan.FromSeconds(1));
var c14 = await ClaimOnly(n14);
Check(c14 != null, "14. after the lease expires the message is claimed again");
if (c14 != null) await Store().CompleteAsync(c14, null, CancellationToken.None);

var n15 = await NewNotification("c15");
var c15 = await ClaimOnly(n15);
await Store().FailAsync(c15!, true, TimeSpan.Zero, "FCM_InvalidArgument", CancellationToken.None);
var r15 = await Row(n15);
Check(r15.Status == "Dead" && r15.Attempts == 1 && r15.Error == "FCM_InvalidArgument", "15. permanent failure: DEAD at the first attempt, safe code only", r15);

var n16 = await NewNotification("c16");
var c16 = await ClaimOnly(n16);
await Store().CompleteAsync(c16!, "provider-1", CancellationToken.None);
var r16 = await Row(n16);
Check(r16.Status == "Sent" && r16.SentAt != null && r16.Token == null && await ClaimOnly(n16) == null, "16. SENT has SentAt, no lease, never claimed again");

var nRa = await NewNotification("retry-after");
var cRa = await ClaimOnly(nRa);
var before = await DbNow();
await Store().FailAsync(cRa!, false, TimeSpan.FromMinutes(3), "FCM_QuotaExceeded", CancellationToken.None);
var rRa = await Row(nRa);
Check(rRa.Status == "Failed" && rRa.Next >= before.AddMinutes(3).AddSeconds(-1), "Retry-After: next attempt not before now + the provider's wait", rRa.Next);
await Exec("""UPDATE "AppNotifications" SET "Status"='Dead',"LastError"='TEST_DONE' WHERE "Id"=@id""", ("id", nRa));

// ---------------------------------------------------------------- 18: channels / recipients
Console.WriteLine("\n== 18. channels and recipients ==");
var ek = $"TEST:{run}:multi";
var user = Guid.NewGuid();
var sms = await NewNotification("c18", "Sms", user, eventKey: ek);
var push = await NewNotification("c18", "Push", user, eventKey: ek);
var otherUser = await NewNotification("c18", "Push", Guid.NewGuid(), eventKey: ek);
var duplicateRefused = false;
try { await NewNotification("c18", "Push", user, eventKey: ek); } catch (PostgresException e) when (e.SqlState == "23505") { duplicateRefused = true; }
Check(duplicateRefused, "18. same event + channel + recipient twice: refused by the unique index");
var nullUserRefused = false;
await Exec("""UPDATE "AppNotifications" SET "UserId"=NULL WHERE "Id"=@id""", ("id", sms));
try
{
    var dup = await NewNotification("c18n", "Sms", null, eventKey: ek);
    await Exec("""UPDATE "AppNotifications" SET "UserId"=NULL WHERE "Id"=@id""", ("id", dup));
}
catch (PostgresException e) when (e.SqlState == "23505") { nullUserRefused = true; }
Check(nullUserRefused, "18. NULL user: still unique (NULLS NOT DISTINCT)");
var counts = new Dictionary<Guid, int>();
var sender18 = new CountingSender(counts);
await new OutboxProcessor(Store(), sender18, options).RunOnceAsync(20);
Check(new[] { sms, push, otherUser }.All(id => counts.GetValueOrDefault(id) == 1) && new[] { sms, push, otherUser }.All(id => Row(id).Result.Status == "Sent"),
    "18. each channel/recipient claimed and sent on its own, once", string.Join(",", new[] { sms, push, otherUser }.Select(id => counts.GetValueOrDefault(id))));

// ---------------------------------------------------------------- several backend instances
Console.WriteLine("\n== several backend instances ==");
var many = new List<Guid>();
for (var i = 0; i < 12; i++) many.Add(await NewNotification("multi-instance"));
var shared = new Dictionary<Guid, int>();
await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => new OutboxProcessor(Store(), new CountingSender(shared), options).RunOnceAsync(50))));
Check(many.All(id => shared.GetValueOrDefault(id) == 1) && many.All(id => Row(id).Result.Status == "Sent"),
    "three processors (instances) at once: every message sent exactly once by the queue", string.Join("", many.Select(id => shared.GetValueOrDefault(id))));

Check(dead.Count >= 3, "DEAD rows reported to the incident callback", dead.Count);
await Exec("""UPDATE "AppNotifications" SET "Status"='Dead',"LastError"='TEST_DONE',"LeaseToken"=NULL,"LeaseUntil"=NULL WHERE "Template" LIKE @t AND "Status" NOT IN ('Sent','Dead')""", ("t", $"Test:{run}:%"));
Console.WriteLine($"\n{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

sealed class CountingSender(Dictionary<Guid, int> counts) : IOutboxSender
{
    public Task<DeliveryResult> SendAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        lock (counts) counts[message.EventKey] = counts.GetValueOrDefault(message.EventKey) + 1;
        return Task.FromResult(DeliveryResult.Success());
    }
}

sealed class BlockingSender(Guid id, CancellationTokenSource stop) : IOutboxSender
{
    public async Task<DeliveryResult> SendAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (message.EventKey == id) stop.Cancel(); // the host shuts down while this send is in flight
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return DeliveryResult.Success();
    }
}

/* Lets the processor claim only the given row (other rows of the run are parked). */
sealed class OnlyStore(IOutboxStore inner, Guid id) : IOutboxStore
{
    public async Task<OutboxClaim?> ClaimAsync(CancellationToken ct)
    {
        for (var i = 0; i < 50; i++)
        {
            var c = await inner.ClaimAsync(ct);
            if (c == null || c.Message.EventKey == id) return c;
            await inner.FailAsync(c, false, TimeSpan.FromHours(1), "TEST_PARKED", ct);
        }
        return null;
    }
    public Task<bool> CompleteAsync(OutboxClaim claim, string? providerMessageId, CancellationToken ct) => inner.CompleteAsync(claim, providerMessageId, ct);
    public Task<bool> FailAsync(OutboxClaim claim, bool permanent, TimeSpan delay, string code, CancellationToken ct) => inner.FailAsync(claim, permanent, delay, code, ct);
}
