using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Saham.Integrations.Wallet;

/* A stand-in wallet provider (phase 9, until EXT-01 is signed). It behaves like a real one:
 *   - a session gives a "provider" link; paying there (FakeWalletController, dev only) sends a
 *     signed webhook (HMAC-SHA256 of the body, header X-Saham-Signature) to our webhook endpoint;
 *   - status queries and refunds answer from memory, with the same idempotency rules;
 *   - Wallet:Fake:RefundFailure = Retryable | Final makes every refund fail, to test UC-22 E1/E2.
 * The state lives in memory: a restart forgets sessions (queries then answer Unknown). */
[ExposeServices(typeof(IWalletProvider), typeof(FakeWalletProvider))]
public class FakeWalletProvider : IWalletProvider, ISingletonDependency
{
    public const string ProviderName = "fake";
    public const string SignatureHeader = "X-Saham-Signature";

    private readonly ConcurrentDictionary<string, (Guid PaymentId, long Amount, WalletPaymentState State)> _payments = new();
    private readonly ConcurrentDictionary<string, WalletRefundResult> _refunds = new();
    private readonly WalletOptions _options;

    public FakeWalletProvider(IOptions<WalletOptions> options)
    {
        _options = options.Value;
    }

    public string Name => ProviderName;

    public Task<WalletSession> CreatePaymentAsync(Guid paymentId, long amount, string idempotencyKey)
    {
        var txnId = "fake_" + paymentId.ToString("N");
        _payments.TryAdd(txnId, (paymentId, amount, WalletPaymentState.Pending));
        return Task.FromResult(new WalletSession(txnId, $"{_options.Fake.PayPageBaseUrl.TrimEnd('/')}/{paymentId}"));
    }

    public Task<WalletPaymentStatus> GetPaymentStatusAsync(string providerTxnId)
    {
        return Task.FromResult(_payments.TryGetValue(providerTxnId, out var p)
            ? new WalletPaymentStatus(p.State, p.State == WalletPaymentState.Succeeded ? p.Amount : null)
            : new WalletPaymentStatus(WalletPaymentState.Unknown, null));
    }

    public Task<WalletRefundResult> RefundAsync(string providerTxnId, long amount, string idempotencyKey)
    {
        if (_refunds.TryGetValue(idempotencyKey, out var done) && done.State == WalletRefundState.Succeeded)
        {
            return Task.FromResult(done);
        }

        var result = _options.Fake.RefundFailure switch
        {
            FakeRefundFailure.Retryable => new WalletRefundResult(WalletRefundState.FailedRetryable, null, "Simulated provider timeout"),
            FakeRefundFailure.Final => new WalletRefundResult(WalletRefundState.FailedFinal, null, "Simulated refund rejected"),
            _ => new WalletRefundResult(WalletRefundState.Succeeded, "fakerf_" + Guid.NewGuid().ToString("N"))
        };
        _refunds[idempotencyKey] = result;
        return Task.FromResult(result);
    }

    public Task<WalletRefundResult?> FindRefundAsync(string idempotencyKey)
    {
        return Task.FromResult(_refunds.TryGetValue(idempotencyKey, out var r) && r.State == WalletRefundState.Succeeded ? r : null);
    }

    public WalletWebhookEvent ParseWebhook(string body, string? signature)
    {
        var valid = signature != null && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Sign(body)), Encoding.ASCII.GetBytes(signature));
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            return new WalletWebhookEvent(
                valid,
                root.GetProperty("eventId").GetString() ?? string.Empty,
                root.GetProperty("type").GetString() ?? string.Empty,
                Guid.TryParse(root.TryGetProperty("reference", out var r) ? r.GetString() : null, out var id) ? id : null,
                root.TryGetProperty("providerTxnId", out var t) ? t.GetString() : null,
                root.TryGetProperty("amount", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetInt64() : null,
                root.TryGetProperty("error", out var e) ? e.GetString() : null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
        {
            return new WalletWebhookEvent(false, string.Empty, string.Empty, null, null, null, "Malformed body");
        }
    }

    // ------------------------------------------------------------------ the "provider side" (dev only)

    /* What the customer does on the provider's page: pay (or fail), which produces a signed webhook body. */
    public (string Body, string Signature) SimulateCustomerPayment(Guid paymentId, bool succeed, long? amountOverride = null, string? eventId = null)
    {
        var txnId = "fake_" + paymentId.ToString("N");
        var amount = amountOverride ?? (_payments.TryGetValue(txnId, out var p) ? p.Amount : 0);
        if (_payments.TryGetValue(txnId, out var existing))
        {
            _payments[txnId] = (existing.PaymentId, existing.Amount, succeed ? WalletPaymentState.Succeeded : WalletPaymentState.Failed);
        }

        var body = JsonSerializer.Serialize(new
        {
            eventId = eventId ?? "evt_" + Guid.NewGuid().ToString("N"),
            type = succeed ? WalletEventTypes.PaymentSucceeded : WalletEventTypes.PaymentFailed,
            reference = paymentId.ToString(),
            providerTxnId = txnId,
            amount,
            error = succeed ? null : "Customer declined"
        });
        return (body, Sign(body));
    }

    /* The customer paid but the webhook never came (UC-05 A1): only a status query will find it. */
    public void SimulateSilentPayment(Guid paymentId)
    {
        var txnId = "fake_" + paymentId.ToString("N");
        if (_payments.TryGetValue(txnId, out var p))
        {
            _payments[txnId] = (p.PaymentId, p.Amount, WalletPaymentState.Succeeded);
        }
    }

    private string Sign(string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.Fake.WebhookSecret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }
}

/* Configuration section "Wallet". */
public class WalletOptions
{
    public FakeWalletOptions Fake { get; set; } = new();
}

public class FakeWalletOptions
{
    public string WebhookSecret { get; set; } = "saham-fake-wallet-dev-secret";

    /* Where the app sends the customer to pay; the fake page is FakeWalletController. */
    public string PayPageBaseUrl { get; set; } = "https://localhost:44343/fake-wallet/pay";

    /* Enables the fake pay page; off outside development. */
    public bool EnablePayPage { get; set; }

    public FakeRefundFailure RefundFailure { get; set; } = FakeRefundFailure.None;
}

public enum FakeRefundFailure
{
    None = 0,
    Retryable = 1,
    Final = 2
}
