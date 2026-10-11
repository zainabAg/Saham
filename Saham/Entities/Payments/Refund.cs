using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Payments;

/* Money going back to the customer's wallet (SRS table refunds, UC-22, BR-041).
 * Created PENDING in the same transaction as its cause (cancellation, item removal, late capture),
 * then sent by RefundWorker: retryable failures wait 1, 5, 15, 60, 180 min; after the last attempt
 * (or a final failure) it goes to MANUAL_REVIEW for the accountant, who retries it or settles it
 * outside the system (RESOLVED_MANUALLY, J-REFUND-MANUAL). Never both: one path sends the money. */
public class Refund : FullAuditedAggregateRoot<Guid>
{
    public const int ErrorMaxLength = 1024;
    public const int ReferenceMaxLength = 128;

    public Guid OrderId { get; private set; }

    public Guid PaymentId { get; private set; }

    public long Amount { get; private set; }

    public RefundReason Reason { get; private set; }

    public RefundStatus Status { get; private set; }

    /* Also sent to the provider, so a retry after a timeout never refunds twice (UC-22 A1). */
    public string IdempotencyKey { get; private set; }

    public string? ProviderRefundId { get; private set; }

    public int Attempts { get; private set; }

    public DateTime? NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    public Guid? RequestedBy { get; private set; }

    public Guid? ResolvedBy { get; private set; }

    public string? ResolutionReference { get; private set; }

    public ManualPaymentChannel? ResolutionChannel { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    protected Refund()
    {
        IdempotencyKey = null!;
    }

    internal Refund(Guid id, Guid orderId, Guid paymentId, long amount, RefundReason reason, string idempotencyKey,
        Guid? requestedBy, DateTime now)
        : base(id)
    {
        if (amount <= 0)
        {
            throw new BusinessException(SahamErrorCodes.InvalidAmount);
        }

        OrderId = orderId;
        PaymentId = paymentId;
        Amount = amount;
        Reason = reason;
        IdempotencyKey = idempotencyKey;
        RequestedBy = requestedBy;
        Status = RefundStatus.Pending;
        NextAttemptAt = now;
    }

    public bool IsDue(DateTime now) =>
        Status is RefundStatus.Pending or RefundStatus.FailedRetryable && NextAttemptAt <= now;

    internal void StartAttempt()
    {
        Status = RefundStatus.Processing;
        Attempts++;
    }

    internal void Succeed(string? providerRefundId, DateTime now)
    {
        Status = RefundStatus.Succeeded;
        ProviderRefundId = providerRefundId;
        CompletedAt = now;
        NextAttemptAt = null;
        LastError = null;
    }

    /* Returns true when the refund is handed to the accountant (MANUAL_REVIEW). */
    internal bool Fail(string error, bool retryable, TimeSpan? retryAfter, DateTime now)
    {
        LastError = error.Length > ErrorMaxLength ? error[..ErrorMaxLength] : error;
        if (retryable && retryAfter.HasValue)
        {
            Status = RefundStatus.FailedRetryable;
            NextAttemptAt = now + retryAfter.Value;
            return false;
        }

        Status = RefundStatus.ManualReview;
        NextAttemptAt = null;
        return true;
    }

    /* The accountant tries the provider again (UC-22 E3). */
    public void Retry(DateTime now)
    {
        if (Status != RefundStatus.ManualReview)
        {
            throw new BusinessException(SahamErrorCodes.InvalidRefundStatus).WithData("status", Status.ToString());
        }

        Status = RefundStatus.Pending;
        Attempts = 0;
        NextAttemptAt = now;
    }

    /* The money was returned outside the system (cash or bank), with a reference as proof. */
    internal void ResolveManually(Guid by, string reference, ManualPaymentChannel channel, DateTime now)
    {
        if (Status != RefundStatus.ManualReview)
        {
            throw new BusinessException(SahamErrorCodes.InvalidRefundStatus).WithData("status", Status.ToString());
        }

        Status = RefundStatus.ResolvedManually;
        ResolvedBy = by;
        ResolutionReference = Check.NotNullOrWhiteSpace(reference, nameof(reference), ReferenceMaxLength);
        ResolutionChannel = channel;
        CompletedAt = now;
    }
}
