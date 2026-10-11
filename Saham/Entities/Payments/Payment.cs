using System;
using Saham.Entities.Orders;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Payments;

/* An electronic payment of an order (SRS table payments, UC-05). One live payment per order
 * (partial unique index). The provider confirms the capture by a signed webhook, or the status
 * poll does (A1). RefundedAmount is what is already promised back: refunds can never exceed the
 * captured amount (BR-042) — concurrent refunds conflict on the ConcurrencyStamp. */
public class Payment : FullAuditedAggregateRoot<Guid>
{
    public const int ProviderMaxLength = 32;

    public Guid OrderId { get; private set; }

    public PaymentMethod Method { get; private set; }

    public string Provider { get; private set; }

    public PaymentStatus Status { get; private set; }

    public long Amount { get; private set; }

    public long CapturedAmount { get; private set; }

    public long RefundedAmount { get; private set; }

    public string? ProviderTxnId { get; private set; }

    /* The session link the app opens to pay (provider page). */
    public string? RedirectUrl { get; private set; }

    public string IdempotencyKey { get; private set; }

    public DateTime ExpiresAt { get; private set; }

    public DateTime? CapturedAt { get; private set; }

    public DateTime? LastCheckedAt { get; private set; }

    public string? FailureReason { get; private set; }

    protected Payment()
    {
        Provider = null!;
        IdempotencyKey = null!;
    }

    internal Payment(Guid id, Guid orderId, string provider, long amount, DateTime expiresAt)
        : base(id)
    {
        OrderId = orderId;
        Method = PaymentMethod.Wallet;
        Provider = provider;
        Amount = amount;
        ExpiresAt = expiresAt;
        IdempotencyKey = $"payment:{orderId:N}:{id:N}";
        Status = PaymentStatus.Initiated;
    }

    public bool IsOpen => Status is PaymentStatus.Initiated or PaymentStatus.Pending;

    internal void MarkSessionOpened(string providerTxnId, string? redirectUrl)
    {
        ProviderTxnId = providerTxnId;
        RedirectUrl = redirectUrl;
        Status = PaymentStatus.Pending;
    }

    /* Also from Failed / Expired: money that arrives late is still captured, then refunded (EX-10). */
    internal void Capture(long amount, string? providerTxnId, DateTime now)
    {
        if (Status == PaymentStatus.Captured)
        {
            return;
        }

        Status = PaymentStatus.Captured;
        CapturedAmount = amount;
        CapturedAt = now;
        ProviderTxnId ??= providerTxnId;
        FailureReason = null;
    }

    internal void Fail(string reason)
    {
        if (IsOpen)
        {
            Status = PaymentStatus.Failed;
            FailureReason = reason.Length > 256 ? reason[..256] : reason;
        }
    }

    internal void Expire()
    {
        if (IsOpen)
        {
            Status = PaymentStatus.Expired;
            FailureReason = "TIMEOUT";
        }
    }

    internal void MarkChecked(DateTime now) => LastCheckedAt = now;

    public long Refundable => CapturedAmount - RefundedAmount;

    /* BR-042: Σ refunds (pending, in progress, done) ≤ captured. */
    internal void AddRefund(long amount)
    {
        if (amount <= 0 || amount > Refundable)
        {
            throw new BusinessException(SahamErrorCodes.RefundExceedsPayment).WithData("refundable", Refundable);
        }

        RefundedAmount += amount;
    }
}
