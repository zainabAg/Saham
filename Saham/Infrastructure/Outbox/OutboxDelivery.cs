using System.Data;
using System.Data.Common;
using System.Text.Json;

namespace Saham.Infrastructure.Outbox;

/* Phase 6 delivery engine from the package (policy + processor), unchanged in behaviour except:
 * - DeliveryResult carries the provider message id and a safe error code (e.g. EXPIRED);
 * - IOutboxStore.CompleteAsync receives that provider id.
 * The store on the existing AppNotifications table is NotificationOutboxStore; the package's own
 * PostgresOutboxStore / outbox_events reference schema is not used (one queue, no competing path). */
public sealed record OutboxMessage(
    Guid EventKey, string EventType, string AggregateType,
    string AggregateId, string PayloadJson);

public sealed record OutboxClaim(long Id, Guid LeaseToken, int Attempt, OutboxMessage Message);

public sealed record OutboxOptions
{
    // Operational defaults, not business rules from the SRS.
    public int MaxAttempts { get; init; } = 10;
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromSeconds(90);
    public TimeSpan SendTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromMinutes(15);

    public void Validate()
    {
        if (MaxAttempts is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(MaxAttempts));
        if (SendTimeout <= TimeSpan.Zero || LeaseDuration <= SendTimeout + TimeSpan.FromSeconds(10)
            || LeaseDuration > TimeSpan.FromDays(1))
            throw new ArgumentException("Lease must exceed send timeout by more than 10 seconds and be at most one day.");
        if (InitialRetryDelay <= TimeSpan.Zero || MaximumRetryDelay < InitialRetryDelay
            || MaximumRetryDelay > TimeSpan.FromDays(1))
            throw new ArgumentException("Invalid retry delay bounds.");
    }
}

/// <summary>Exponential backoff with equal jitter: [half the cap, full cap].</summary>
public static class OutboxRetryPolicy
{
    public static TimeSpan GetDelay(int failedAttempt, OutboxOptions options, double randomUnit)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (failedAttempt < 1) throw new ArgumentOutOfRangeException(nameof(failedAttempt));
        if (!double.IsFinite(randomUnit) || randomUnit < 0 || randomUnit >= 1)
            throw new ArgumentOutOfRangeException(nameof(randomUnit));

        // Bound the exponent; never overflow when recovering a malformed attempt count.
        double cap = Math.Min(options.MaximumRetryDelay.TotalMilliseconds,
            options.InitialRetryDelay.TotalMilliseconds * Math.Pow(2, Math.Min(failedAttempt - 1, 30)));
        return TimeSpan.FromMilliseconds(cap * (0.5 + 0.5 * randomUnit));
    }
}

public enum DeliveryDisposition { RetryableFailure, PermanentFailure }
public sealed record DeliveryResult(bool Accepted, DeliveryDisposition FailureDisposition,
    TimeSpan? RetryAfter = null, string? ProviderMessageId = null, string? ErrorCode = null)
{
    public static DeliveryResult Success(string? providerMessageId = null) =>
        new(true, DeliveryDisposition.RetryableFailure, ProviderMessageId: providerMessageId);
    public static DeliveryResult Retry() => new(false, DeliveryDisposition.RetryableFailure);
    public static DeliveryResult Retry(TimeSpan retryAfter)
    {
        if (retryAfter <= TimeSpan.Zero || retryAfter > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(retryAfter));
        return new(false, DeliveryDisposition.RetryableFailure, retryAfter);
    }
    public static DeliveryResult PermanentFailure(string? errorCode = null) =>
        new(false, DeliveryDisposition.PermanentFailure, ErrorCode: errorCode);
}

public interface IOutboxSender
{
    // One message is ONE externally observable send, e.g. one channel + one recipient.
    // Use Message.EventKey as the provider idempotency key whenever supported.
    // Success means durable acceptance, not necessarily arrival at the recipient.
    // Must observe cancellation and have its own bounded transport timeout.
    Task<DeliveryResult> SendAsync(OutboxMessage message, CancellationToken cancellationToken);
}

public interface IOutboxStore
{
    Task<OutboxClaim?> ClaimAsync(CancellationToken cancellationToken);
    Task<bool> CompleteAsync(OutboxClaim claim, string? providerMessageId, CancellationToken cancellationToken);
    Task<bool> FailAsync(OutboxClaim claim, bool permanent, TimeSpan delay,
        string safeErrorCode, CancellationToken cancellationToken);
}

public sealed record OutboxRunResult(int Claimed, int Accepted, int Failed, int LostLeases);

/// <summary>
/// Framework-independent runner; invoke from an ABP periodic worker or HostedService.
/// Delivery is at-least-once: a crash after acceptance but before ack can cause a duplicate.
/// </summary>
public sealed class OutboxProcessor
{
    private readonly IOutboxStore _store;
    private readonly IOutboxSender _sender;
    private readonly OutboxOptions _options;

    public OutboxProcessor(IOutboxStore store, IOutboxSender sender, OutboxOptions options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public async Task<OutboxRunResult> RunOnceAsync(int maximumMessages = 50,
        CancellationToken cancellationToken = default)
    {
        if (maximumMessages is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(maximumMessages));
        int claimed = 0, accepted = 0, failed = 0, lost = 0;
        while (claimed < maximumMessages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OutboxClaim? claim = await _store.ClaimAsync(cancellationToken);
            if (claim is null) break;
            claimed++;
            DeliveryResult delivery;
            string safeError = "PROVIDER_RETRYABLE_FAILURE";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.SendTimeout);
            try
            {
                delivery = await _sender.SendAsync(claim.Message, timeout.Token);
                // Detect an invalid sender implementation while preserving the outgoing event.
                if (delivery is null)
                    throw new InvalidOperationException("Sender returned no delivery result.");
                if (delivery.RetryAfter is { } retryAfter
                    && (retryAfter <= TimeSpan.Zero || retryAfter > TimeSpan.FromDays(1)))
                    throw new InvalidOperationException("Sender returned an invalid retry interval.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Leave the lease intact. Another worker recovers it after expiration.
                throw;
            }
            catch (OperationCanceledException)
            {
                delivery = DeliveryResult.Retry();
                safeError = "PROVIDER_TIMEOUT_OUTCOME_UNKNOWN";
            }
            catch (Exception)
            {
                // Do not persist exception messages: they can contain OTPs/phone numbers/tokens.
                delivery = DeliveryResult.Retry();
                safeError = "SENDER_EXCEPTION";
            }

            // DB acknowledgement failures intentionally propagate to the hosting worker's
            // health/error handling; the lease later expires and makes recovery possible.
            if (delivery.Accepted)
            {
                if (await _store.CompleteAsync(claim, delivery.ProviderMessageId, cancellationToken)) accepted++;
                else lost++;
            }
            else
            {
                bool permanent = delivery.FailureDisposition == DeliveryDisposition.PermanentFailure;
                if (permanent) safeError = delivery.ErrorCode ?? "PROVIDER_PERMANENT_FAILURE";
                else if (delivery.ErrorCode != null) safeError = delivery.ErrorCode;
                var delay = OutboxRetryPolicy.GetDelay(claim.Attempt, _options, Random.Shared.NextDouble());
                // A provider-requested waiting period is a minimum, not a cap.
                if (delivery.RetryAfter is { } retryAfter && retryAfter > delay)
                    delay = retryAfter;
                if (await _store.FailAsync(claim, permanent, delay, safeError, cancellationToken)) failed++;
                else lost++;
            }
        }
        return new OutboxRunResult(claimed, accepted, failed, lost);
    }
}
