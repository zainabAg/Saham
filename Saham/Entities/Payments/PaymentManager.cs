using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Saham.Entities.Ledger;
using Saham.Entities.Notifications;
using Saham.Entities.Orders;
using Saham.Entities.Support;
using Saham.Integrations.Wallet;
using Saham.Settings;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Settings;

namespace Saham.Entities.Payments;

/* Wallet payments and refunds (UC-05, UC-22, BR-022, BR-041, BR-042):
 *   start   — the order is PENDING_PAYMENT and a provider session is opened (15 min to pay);
 *   webhook — signed, stored once per provider event id; a capture posts J-WALLET-CAPTURED and sends
 *             the order to the restaurant, all in one transaction. A wrong amount captures nothing and
 *             opens a financial incident (E5); a capture after the order was cancelled is refunded (E4);
 *   expiry  — no payment in time → the payment expires and the order is cancelled (PAYMENT_TIMEOUT);
 *   refunds — whatever the order no longer needs goes back automatically (SyncRefundsAsync). */
public class PaymentManager : DomainService
{
    private readonly IRepository<Payment, Guid> _paymentRepository;
    private readonly IRepository<PaymentEvent, Guid> _eventRepository;
    private readonly IRepository<Refund, Guid> _refundRepository;
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IWalletProvider _provider;
    private readonly PaymentPostingService _posting;
    private readonly IncidentManager _incidentManager;
    private readonly OutboxManager _outbox;
    private readonly ISettingProvider _settingProvider;

    public PaymentManager(
        IRepository<Payment, Guid> paymentRepository,
        IRepository<PaymentEvent, Guid> eventRepository,
        IRepository<Refund, Guid> refundRepository,
        IRepository<Order, Guid> orderRepository,
        IWalletProvider provider,
        PaymentPostingService posting,
        IncidentManager incidentManager,
        OutboxManager outbox,
        ISettingProvider settingProvider)
    {
        _paymentRepository = paymentRepository;
        _eventRepository = eventRepository;
        _refundRepository = refundRepository;
        _orderRepository = orderRepository;
        _provider = provider;
        _posting = posting;
        _incidentManager = incidentManager;
        _outbox = outbox;
        _settingProvider = settingProvider;
    }

    // ------------------------------------------------------------------ start

    /* UC-05 steps 1–2. Nothing to pay (in-app credit covers it all): the order goes straight on. */
    public async Task<Payment?> StartAsync(Order order)
    {
        if (order.PaymentMethod != PaymentMethod.Wallet || order.Status != OrderStatus.PendingPayment)
        {
            return null;
        }

        if (order.AmountDue == 0)
        {
            order.ConfirmPayment(Clock.Now, GuidGenerator.Create);
            return null;
        }

        var minutes = await _settingProvider.GetAsync<int>(SahamSettings.Orders.WalletPaymentTimeoutMinutes);
        var payment = new Payment(GuidGenerator.Create(), order.Id, _provider.Name, order.AmountDue, Clock.Now.AddMinutes(minutes));
        var session = await _provider.CreatePaymentAsync(payment.Id, payment.Amount, payment.IdempotencyKey);
        payment.MarkSessionOpened(session.ProviderTxnId, session.RedirectUrl);
        return await _paymentRepository.InsertAsync(payment, autoSave: true);
    }

    // ------------------------------------------------------------------ webhook

    public async Task<WebhookOutcome> HandleWebhookAsync(string providerName, string body, string? signature)
    {
        if (providerName != _provider.Name)
        {
            return WebhookOutcome.UnknownProvider;
        }

        var e = _provider.ParseWebhook(body, signature);
        // An unsigned (or forged) body is kept for the record, but never under the real event id:
        // otherwise a forgery could block the genuine event as a "duplicate".
        var eventId = e.SignatureValid && !e.EventId.IsNullOrWhiteSpace()
            ? e.EventId
            : "invalid_" + GuidGenerator.Create().ToString("N");
        if (await _eventRepository.AnyAsync(x => x.Provider == providerName && x.ProviderEventId == eventId))
        {
            return WebhookOutcome.Duplicate; // E3: already handled
        }

        var stored = await _eventRepository.InsertAsync(
            new PaymentEvent(GuidGenerator.Create(), providerName, eventId, e.EventType, body, e.SignatureValid, Clock.Now), autoSave: true);
        if (!e.SignatureValid)
        {
            stored.MarkProcessed(null, Clock.Now, "INVALID_SIGNATURE");
            return WebhookOutcome.InvalidSignature;
        }

        var payment = e.PaymentId.HasValue
            ? await _paymentRepository.FindAsync(e.PaymentId.Value)
            : await _paymentRepository.FirstOrDefaultAsync(p => p.ProviderTxnId == e.ProviderTxnId);
        if (payment == null)
        {
            stored.MarkProcessed(null, Clock.Now, "PAYMENT_NOT_FOUND");
            return WebhookOutcome.Ignored;
        }

        var error = e.EventType switch
        {
            WalletEventTypes.PaymentSucceeded => await CaptureAsync(payment, e.Amount ?? -1, e.ProviderTxnId),
            WalletEventTypes.PaymentFailed => await FailAsync(payment, e.Error ?? "DECLINED"),
            _ => "UNKNOWN_EVENT_TYPE"
        };
        stored.MarkProcessed(payment.Id, Clock.Now, error);
        await _eventRepository.UpdateAsync(stored);
        return error == null ? WebhookOutcome.Processed : WebhookOutcome.Ignored;
    }

    /* Returns an error code when the capture is refused. */
    public async Task<string?> CaptureAsync(Payment payment, long amount, string? providerTxnId)
    {
        if (payment.Status == PaymentStatus.Captured)
        {
            return null;
        }

        var order = await _orderRepository.GetAsync(payment.OrderId, includeDetails: false);
        if (amount != payment.Amount)
        {
            // E5: the order is not confirmed; the accountant looks at it.
            await _incidentManager.OpenAsync(IncidentType.PaymentAmountMismatch, IncidentSeverity.High, payment.Id,
                orderId: order.Id, restaurantId: order.RestaurantId);
            return "AMOUNT_MISMATCH";
        }

        payment.Capture(amount, providerTxnId, Clock.Now);
        await _paymentRepository.UpdateAsync(payment, autoSave: true);
        await _posting.PostCapturedAsync(payment, order);

        if (order.Status == OrderStatus.PendingPayment)
        {
            order.ConfirmPayment(Clock.Now, GuidGenerator.Create);
            await _orderRepository.UpdateAsync(order);
        }
        else
        {
            // E4 / EX-10: the money came after the order was cancelled (or timed out) — give it all back.
            await SyncRefundsAsync(order, RefundReason.LateCapture);
        }

        return null;
    }

    public async Task<string?> FailAsync(Payment payment, string reason)
    {
        if (!payment.IsOpen)
        {
            return null;
        }

        payment.Fail(reason);
        await _paymentRepository.UpdateAsync(payment);

        var order = await _orderRepository.GetAsync(payment.OrderId, includeDetails: false);
        if (order.Status == OrderStatus.PendingPayment)
        {
            order.CancelBySystem(CancelReason.PaymentFailed, Clock.Now, GuidGenerator.Create);
            await _orderRepository.UpdateAsync(order);
        }

        return null;
    }

    /* E2: no payment within the time limit. The provider is asked one last time first (A1). */
    public async Task ExpireOrCaptureAsync(Payment payment)
    {
        if (!payment.IsOpen)
        {
            return;
        }

        if (await PollAsync(payment))
        {
            return;
        }

        payment.Expire();
        await _paymentRepository.UpdateAsync(payment);
        var order = await _orderRepository.GetAsync(payment.OrderId, includeDetails: false);
        if (order.Status == OrderStatus.PendingPayment)
        {
            order.CancelBySystem(CancelReason.PaymentTimeout, Clock.Now, GuidGenerator.Create);
            await _orderRepository.UpdateAsync(order);
        }
    }

    /* A1: no webhook yet — ask the provider. Returns true when the payment was settled either way. */
    public async Task<bool> PollAsync(Payment payment)
    {
        payment.MarkChecked(Clock.Now);
        if (payment.ProviderTxnId == null)
        {
            return false;
        }

        var status = await _provider.GetPaymentStatusAsync(payment.ProviderTxnId);
        switch (status.State)
        {
            case WalletPaymentState.Succeeded:
                return await CaptureAsync(payment, status.Amount ?? -1, payment.ProviderTxnId) == null;
            case WalletPaymentState.Failed:
                await FailAsync(payment, status.Error ?? "DECLINED");
                return true;
            default:
                await _paymentRepository.UpdateAsync(payment);
                return false;
        }
    }

    // ------------------------------------------------------------------ refunds

    /* BR-041: money the order no longer needs goes back, in the transaction of the change:
     *   finished without a charge (cancelled, driver incident) → everything still held for the order
     *     (after the late-cancel / driver-pay charges were posted from it);
     *   still running (an item removed or replaced) → captured − already refunded − new amount due.
     * Delivered and "not received" consume everything: nothing to give back. */
    public async Task<Refund?> SyncRefundsAsync(Order order, RefundReason reason, Guid? requestedBy = null)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(p => p.OrderId == order.Id && p.Status == PaymentStatus.Captured);
        if (payment == null)
        {
            return null;
        }

        long amount;
        if (order.Status == OrderStatus.Cancelled ||
            (order.Status == OrderStatus.DeliveryFailed && order.FailureReason == FailureReason.DriverIncident))
        {
            amount = await _posting.PrepaidRemainingAsync(order.Id);
        }
        else if (!order.IsFinal)
        {
            amount = payment.Refundable - order.AmountDue;
        }
        else
        {
            return null;
        }

        amount = Math.Min(amount, payment.Refundable);
        if (amount <= 0)
        {
            return null;
        }

        payment.AddRefund(amount); // BR-042, guarded by the payment's ConcurrencyStamp
        await _paymentRepository.UpdateAsync(payment);

        var sequence = await _refundRepository.CountAsync(r => r.OrderId == order.Id) + 1;
        var refund = await _refundRepository.InsertAsync(new Refund(GuidGenerator.Create(), order.Id, payment.Id, amount, reason,
            $"refund:{order.Id:N}:{reason.ToString().ToLowerInvariant()}:{sequence}", requestedBy, Clock.Now), autoSave: true);
        await _posting.PostRefundInitiatedAsync(refund, order);
        return refund;
    }

    /* One attempt by RefundWorker. The provider is asked first whether this key was already refunded (A1). */
    public async Task ProcessRefundAsync(Refund refund)
    {
        if (!refund.IsDue(Clock.Now))
        {
            return;
        }

        var payment = await _paymentRepository.GetAsync(refund.PaymentId);
        var order = await _orderRepository.GetAsync(refund.OrderId, includeDetails: false);
        refund.StartAttempt();

        WalletRefundResult result;
        try
        {
            result = await _provider.FindRefundAsync(refund.IdempotencyKey)
                     ?? await _provider.RefundAsync(payment.ProviderTxnId!, refund.Amount, refund.IdempotencyKey);
        }
        catch (Exception ex)
        {
            result = new WalletRefundResult(WalletRefundState.FailedRetryable, null, ex.Message);
        }

        if (result.State == WalletRefundState.Succeeded)
        {
            refund.Succeed(result.ProviderRefundId, Clock.Now);
            await _posting.PostRefundSettledAsync(refund, order);
            await _outbox.EnqueueAsync(OutboxEventTypes.RefundSucceeded, OutboxAggregateTypes.Order, order.Id,
                new Dictionary<string, string?> { ["orderId"] = order.Id.ToString(), ["amount"] = refund.Amount.ToString() },
                refund.Id.ToString("N"));
            return;
        }

        var delays = await RetryDelaysAsync();
        var retryAfter = result.State == WalletRefundState.FailedRetryable && refund.Attempts <= delays.Count
            ? delays[refund.Attempts - 1]
            : (TimeSpan?)null;
        if (refund.Fail(result.Error ?? result.State.ToString(), retryAfter.HasValue, retryAfter, Clock.Now))
        {
            // E2: the accountant takes over; the customer is told it is being handled.
            await _incidentManager.OpenAsync(IncidentType.RefundFailed, IncidentSeverity.High, refund.Id,
                orderId: order.Id, restaurantId: order.RestaurantId);
            await _outbox.EnqueueAsync(OutboxEventTypes.RefundFailedFinal, OutboxAggregateTypes.Order, order.Id,
                new Dictionary<string, string?> { ["orderId"] = order.Id.ToString(), ["amount"] = refund.Amount.ToString() },
                refund.Id.ToString("N"));
        }
    }

    /* UC-22 E3: the accountant returned the money outside the system. */
    public async Task ResolveRefundManuallyAsync(Refund refund, Guid accountantId, string reference, ManualPaymentChannel channel)
    {
        refund.ResolveManually(accountantId, reference, channel, Clock.Now);
        var order = await _orderRepository.GetAsync(refund.OrderId, includeDetails: false);
        await _posting.PostRefundManualAsync(refund, order, channel, accountantId);
        await _incidentManager.AutoResolveAsync(IncidentType.RefundFailed, refund.Id, "RESOLVED_MANUALLY");
    }

    /* "60,300,900,3600,10800" seconds by default: 1, 5, 15, 60, 180 minutes (UC-22 E1). */
    private async Task<List<TimeSpan>> RetryDelaysAsync()
    {
        var raw = await _settingProvider.GetOrNullAsync(SahamSettings.Payments.RefundRetrySeconds) ?? string.Empty;
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => TimeSpan.FromSeconds(int.Parse(x)))
            .ToList();
    }
}

public enum WebhookOutcome
{
    Processed = 0,
    Duplicate = 1,
    Ignored = 2,
    InvalidSignature = 3,
    UnknownProvider = 4
}
