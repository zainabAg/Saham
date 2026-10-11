using System;
using System.Threading.Tasks;

namespace Saham.Integrations.Wallet;

/* The e-wallet provider (SRS WalletProvider, EXT-01: not chosen yet). The design follows the
 * usual pattern: a payment session → a signed webhook → a status query → a refund by API with an
 * idempotency key. FakeWalletProvider stands in until the real provider is signed. */
public interface IWalletProvider
{
    string Name { get; }

    Task<WalletSession> CreatePaymentAsync(Guid paymentId, long amount, string idempotencyKey);

    /* UC-05 A1: when no webhook arrives, the payment is polled. */
    Task<WalletPaymentStatus> GetPaymentStatusAsync(string providerTxnId);

    Task<WalletRefundResult> RefundAsync(string providerTxnId, long amount, string idempotencyKey);

    /* UC-22 A1: asked before every retry, so a refund that went through but timed out is not sent twice. */
    Task<WalletRefundResult?> FindRefundAsync(string idempotencyKey);

    /* Checks the signature and reads the event; never throws on a bad signature. */
    WalletWebhookEvent ParseWebhook(string body, string? signature);
}

public record WalletSession(string ProviderTxnId, string? RedirectUrl);

public enum WalletPaymentState
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Unknown = 3
}

public record WalletPaymentStatus(WalletPaymentState State, long? Amount, string? Error = null);

public enum WalletRefundState
{
    Succeeded = 0,
    FailedRetryable = 1,
    FailedFinal = 2
}

public record WalletRefundResult(WalletRefundState State, string? ProviderRefundId, string? Error = null);

public static class WalletEventTypes
{
    public const string PaymentSucceeded = "payment.succeeded";
    public const string PaymentFailed = "payment.failed";
}

public record WalletWebhookEvent(
    bool SignatureValid,
    string EventId,
    string EventType,
    Guid? PaymentId,
    string? ProviderTxnId,
    long? Amount,
    string? Error);
