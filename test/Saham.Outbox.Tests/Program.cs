using System.Net.Http.Headers;
using Saham.Entities.Notifications;
using Saham.Infrastructure.Outbox;

// Phase 6 package tests, ported: policy + OutboxProcessor against the project's copy (Infrastructure/Outbox).
// Not ported: the two OutboxBackgroundService tests — the project runs the processor from its ABP worker
// (NotificationDispatchWorker); resuming after a DB failure is covered by the PostgreSQL integration run.
// These exercise policy/orchestration, NOT PostgreSQL concurrency.
int passed = 0;
var options = new OutboxOptions();

void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + name);
    passed++;
    Console.WriteLine("PASS: " + name);
}

void Throws(Action action, string name)
{
    bool threw = false;
    try { action(); } catch (ArgumentException) { threw = true; }
    Check(threw, name);
}

Check(OutboxRetryPolicy.GetDelay(1, options, 0) == TimeSpan.FromSeconds(2.5), "First retry lower jitter bound");
Check(OutboxRetryPolicy.GetDelay(2, options, 0) == TimeSpan.FromSeconds(5), "Second retry doubles the cap");
Check(OutboxRetryPolicy.GetDelay(1000, options, 0.99) < options.MaximumRetryDelay, "Large attempt stays below cap");
Check(OutboxRetryPolicy.GetDelay(1000, options, 0) == options.MaximumRetryDelay / 2, "Capped lower jitter bound");
Throws(() => OutboxRetryPolicy.GetDelay(0, options, 0), "Reject zero attempt");
Throws(() => OutboxRetryPolicy.GetDelay(1, options, double.NaN), "Reject non-finite random sample");
Throws(() => OutboxRetryPolicy.GetDelay(1, options, 1), "Reject random upper endpoint");
Throws(() => new OutboxOptions { LeaseDuration = TimeSpan.FromSeconds(25) }.Validate(), "Lease has safety margin");

var empty = new FakeStore();
var emptySender = new FakeSender((_, _) => Task.FromResult(DeliveryResult.Success()));
var emptyResult = await new OutboxProcessor(empty, emptySender, options).RunOnceAsync();
Check(emptyResult.Claimed == 0 && emptySender.Calls == 0, "Empty queue makes no provider call");

var success = new FakeStore(NewClaim());
Guid seenKey = Guid.Empty;
var sender = new FakeSender((message, _) =>
{
    seenKey = message.EventKey;
    return Task.FromResult(DeliveryResult.Success());
});
var result = await new OutboxProcessor(success, sender, options).RunOnceAsync();
Check(result.Accepted == 1 && success.Completed == 1 && success.Failed == 0, "Accepted message acknowledges once");
Check(seenKey == success.LastClaim!.Message.EventKey, "Stable event key reaches sender unchanged");

var retry = new FakeStore(NewClaim());
result = await new OutboxProcessor(retry,
    new FakeSender((_, _) => Task.FromResult(DeliveryResult.Retry())), options).RunOnceAsync();
Check(result.Failed == 1 && retry.Completed == 0 && !retry.Permanent, "Retryable failure never marks success");
Check(retry.Delay >= TimeSpan.FromSeconds(2.5) && retry.Delay < TimeSpan.FromSeconds(5), "Retry uses bounded jitter");

var permanent = new FakeStore(NewClaim());
await new OutboxProcessor(permanent,
    new FakeSender((_, _) => Task.FromResult(DeliveryResult.PermanentFailure())), options).RunOnceAsync();
Check(permanent.Permanent && permanent.Error == "PROVIDER_PERMANENT_FAILURE", "Permanent failure retained as DEAD by store");

var exceptional = new FakeStore(NewClaim());
await new OutboxProcessor(exceptional,
    new FakeSender((_, _) => throw new Exception("secret phone OTP token")), options).RunOnceAsync();
Check(exceptional.Error == "SENDER_EXCEPTION" && exceptional.Completed == 0, "Exception detail never persisted");

var timeout = new FakeStore(NewClaim());
await new OutboxProcessor(timeout,
    new FakeSender((_, _) => throw new OperationCanceledException()), options).RunOnceAsync();
Check(timeout.Error == "PROVIDER_TIMEOUT_OUTCOME_UNKNOWN", "Provider timeout remains retryable");

var stale = new FakeStore(NewClaim()) { OwnsLease = false };
result = await new OutboxProcessor(stale,
    new FakeSender((_, _) => Task.FromResult(DeliveryResult.Success())), options).RunOnceAsync();
Check(result.LostLeases == 1 && result.Accepted == 0, "Lost lease is not counted as persisted success");

var bounded = new FakeStore(NewClaim(), NewClaim());
result = await new OutboxProcessor(bounded,
    new FakeSender((_, _) => Task.FromResult(DeliveryResult.Success())), options).RunOnceAsync(1);
Check(result.Claimed == 1 && bounded.Remaining == 1, "Run obeys its maximum message limit");

using var stop = new CancellationTokenSource();
var stopped = new FakeStore(NewClaim());
bool cancelled = false;
try
{
    await new OutboxProcessor(stopped, new FakeSender((_, _) =>
    {
        stop.Cancel();
        throw new OperationCanceledException(stop.Token);
    }), options).RunOnceAsync(cancellationToken: stop.Token);
}
catch (OperationCanceledException) { cancelled = true; }
Check(cancelled && stopped.Completed == 0 && stopped.Failed == 0, "Shutdown leaves claim for lease recovery");

var brokenAck = new FakeStore(NewClaim()) { ThrowOnAck = true };
bool ackFailure = false;
try
{
    await new OutboxProcessor(brokenAck,
        new FakeSender((_, _) => Task.FromResult(DeliveryResult.Success())), options).RunOnceAsync();
}
catch (InvalidOperationException) { ackFailure = true; }
Check(ackFailure && brokenAck.Failed == 0, "Database ack failure propagates without false failure update");
var providerDelay = new FakeStore(NewClaim());
await new OutboxProcessor(providerDelay,
    new FakeSender((_, _) => Task.FromResult(DeliveryResult.Retry(TimeSpan.FromMinutes(3)))), options).RunOnceAsync();
Check(providerDelay.Delay == TimeSpan.FromMinutes(3), "Honor provider Retry-After minimum");
Throws(() => DeliveryResult.Retry(TimeSpan.Zero), "Reject zero Retry-After");
Throws(() => DeliveryResult.Retry(TimeSpan.FromDays(2)), "Reject excessive Retry-After");

var missingResult = new FakeStore(NewClaim());
await new OutboxProcessor(missingResult,
    new FakeSender((_, _) => Task.FromResult<DeliveryResult>(null!)), options).RunOnceAsync();
Check(missingResult.Error == "SENDER_EXCEPTION" && missingResult.Completed == 0,
    "Invalid sender result leaves event retryable");
// Project adaptations: provider id and safe codes pass through; Retry-After header conversion.
var withId = new FakeStore(NewClaim());
await new OutboxProcessor(withId, new FakeSender((_, _) => Task.FromResult(DeliveryResult.Success("fcm-123"))), options).RunOnceAsync();
Check(withId.ProviderMessageId == "fcm-123", "Provider message id reaches the store");
var expired = new FakeStore(NewClaim());
await new OutboxProcessor(expired, new FakeSender((_, _) => Task.FromResult(DeliveryResult.PermanentFailure("EXPIRED"))), options).RunOnceAsync();
Check(expired.Permanent && expired.Error == "EXPIRED", "Permanent failure keeps the sender's safe code");
var coded = new FakeStore(NewClaim());
await new OutboxProcessor(coded, new FakeSender((_, _) => Task.FromResult(new DeliveryResult(false, DeliveryDisposition.RetryableFailure, ErrorCode: "FCM_Unavailable"))), options).RunOnceAsync();
Check(!coded.Permanent && coded.Error == "FCM_Unavailable", "Retryable failure keeps the sender's safe code");
Check(NotificationSender.RetryAfterOf(new RetryConditionHeaderValue(TimeSpan.FromSeconds(120))) == TimeSpan.FromSeconds(120), "Retry-After seconds header");
var at = DateTimeOffset.UtcNow;
Check(NotificationSender.RetryAfterOf(new RetryConditionHeaderValue(at.AddMinutes(5)), at) == TimeSpan.FromMinutes(5), "Retry-After date header");
Check(NotificationSender.RetryAfterOf(new RetryConditionHeaderValue(TimeSpan.FromDays(3))) == TimeSpan.FromDays(1), "Retry-After longer than a day is capped at a day, never shortened below it");
Check(NotificationSender.RetryAfterOf(null) == null && NotificationSender.RetryAfterOf(new RetryConditionHeaderValue(at.AddMinutes(-1)), at) == null, "Missing or past Retry-After is ignored");
Console.WriteLine($"{passed} tests passed.");

static OutboxClaim NewClaim() => new(1, Guid.NewGuid(), 1,
    new OutboxMessage(Guid.NewGuid(), "ORDER_PLACED", "ORDER", "123", "{\"orderId\":123}"));

sealed class FakeSender(Func<OutboxMessage, CancellationToken, Task<DeliveryResult>> callback) : IOutboxSender
{
    public int Calls { get; private set; }
    public Task<DeliveryResult> SendAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        Calls++;
        return callback(message, cancellationToken);
    }
}

sealed class FakeStore(params OutboxClaim[] initial) : IOutboxStore
{
    private readonly Queue<OutboxClaim> _queue = new(initial);
    public int Remaining => _queue.Count;
    public int Completed { get; private set; }
    public int Failed { get; private set; }
    public bool OwnsLease { get; init; } = true;
    public bool ThrowOnAck { get; init; }
    public bool Permanent { get; private set; }
    public TimeSpan Delay { get; private set; }
    public string? Error { get; private set; }
    public int ClaimFailuresRemaining { get; set; }
    public TaskCompletionSource<bool>? CompletionSignal { get; set; }
    public OutboxClaim? LastClaim { get; private set; }

    public Task<OutboxClaim?> ClaimAsync(CancellationToken cancellationToken)
    {
        if (ClaimFailuresRemaining > 0)
        {
            ClaimFailuresRemaining--;
            throw new InvalidOperationException("Simulated DB outage");
        }
        // Fixed from the package: keep the last real claim; the processor's final empty claim used to null it.
        if (_queue.Count == 0) return Task.FromResult<OutboxClaim?>(null);
        LastClaim = _queue.Dequeue();
        return Task.FromResult<OutboxClaim?>(LastClaim);
    }
    public string? ProviderMessageId { get; private set; }
    public Task<bool> CompleteAsync(OutboxClaim claim, string? providerMessageId, CancellationToken cancellationToken)
    {
        if (ThrowOnAck) throw new InvalidOperationException("DB unavailable after provider acceptance");
        Completed++;
        ProviderMessageId = providerMessageId;
        CompletionSignal?.TrySetResult(true);
        return Task.FromResult(OwnsLease);
    }
    public Task<bool> FailAsync(OutboxClaim claim, bool permanent, TimeSpan delay,
        string safeErrorCode, CancellationToken cancellationToken)
    {
        Failed++;
        Permanent = permanent;
        Delay = delay;
        Error = safeErrorCode;
        return Task.FromResult(OwnsLease);
    }
}
