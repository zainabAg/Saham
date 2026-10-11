using System;
using System.Linq;
using System.Threading.Tasks;
using Saham.Entities.Orders;
using Saham.Entities.Payments;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using A = Saham.Entities.Ledger.LedgerAccountCode;

namespace Saham.Entities.Ledger;

/* Wallet money in the ledger (SRS 12.4, 12.5). Captured money is held as CUSTOMER_PREPAID until the
 * order ends: delivery or "not received" consume it (revenue), a cancellation or a removed item
 * sends the rest back (REFUND_PAYABLE → WALLET_CLEARING when the provider pays it).
 * Every prepaid line carries the order id, so "what is left for this order" is one sum. */
public class PaymentPostingService : DomainService
{
    private readonly LedgerManager _ledger;
    private readonly IRepository<JournalLine, Guid> _lineRepository;

    public PaymentPostingService(LedgerManager ledger, IRepository<JournalLine, Guid> lineRepository)
    {
        _ledger = ledger;
        _lineRepository = lineRepository;
    }

    /* J-WALLET-CAPTURED — Dr WALLET_CLEARING / Cr CUSTOMER_PREPAID (amount captured). */
    public async Task PostCapturedAsync(Payment payment, Order order)
    {
        await _ledger.PostAsync(JournalEntryType.WalletCaptured, JournalSourceType.Payment, payment.Id,
            $"payment:{payment.Id:N}:captured", $"Wallet payment captured, order #{order.Number}",
            await _ledger.BusinessDateAsync(payment.CapturedAt ?? Clock.Now, order.CityId), new[]
            {
                (await _ledger.CompanyAccountAsync(A.WalletClearing), payment.CapturedAmount, 0L, (Guid?)order.Id),
                (await _ledger.CompanyAccountAsync(A.CustomerPrepaid), 0L, payment.CapturedAmount, order.Id)
            });
    }

    /* J-WALLET-DELIVERED — like J-CASH-DELIVERED, but the money comes from the prepaid hold:
     *   Dr CUSTOMER_PREPAID due, Dr CUSTOMER_BALANCE credit, Dr DISCOUNT_EXPENSE K_c
     *   Cr RESTAURANT_PAYABLE R, Cr COMMISSION C, Cr DELIVERY_FEE Dfee, Cr CUSTOMER_BALANCE debt.
     * "Not received" with a wallet is the same entry under its own key (DEC-06: no refund). */
    public async Task PostWalletDeliveredAsync(Order order, bool notReceived)
    {
        var customer = await _ledger.PartyAccountAsync(A.CustomerBalance, order.CustomerId);
        await _ledger.PostAsync(notReceived ? JournalEntryType.NotReceived : JournalEntryType.WalletDelivered,
            JournalSourceType.Order, order.Id,
            $"order:{order.Id:N}:{(notReceived ? "not_received" : "delivered")}",
            $"Order #{order.Number} {(notReceived ? "not received" : "delivered")} (wallet)",
            await _ledger.BusinessDateAsync(order.DeliveredAt ?? Clock.Now, order.CityId), new[]
            {
                (await _ledger.CompanyAccountAsync(A.CustomerPrepaid), order.AmountDue, 0L, (Guid?)order.Id),
                (customer, order.CreditApplied, 0L, order.Id),
                (await _ledger.CompanyAccountAsync(A.DiscountExpense), order.CompanyDiscount, 0L, order.Id),
                (await _ledger.PartyAccountAsync(A.RestaurantPayable, order.RestaurantId), 0L, order.RestaurantNetAmount, order.Id),
                (await _ledger.CompanyAccountAsync(A.CommissionRevenue), 0L, order.CommissionAmount, order.Id),
                (await _ledger.CompanyAccountAsync(A.DeliveryFeeRevenue), 0L, order.DeliveryFeeCharged, order.Id),
                (customer, 0L, order.DebtCollected, order.Id)
            });
    }

    /* J-REFUND-INITIATED — Dr CUSTOMER_PREPAID / Cr REFUND_PAYABLE (in the cancellation transaction). */
    public async Task PostRefundInitiatedAsync(Refund refund, Order order)
    {
        await _ledger.PostAsync(JournalEntryType.RefundInitiated, JournalSourceType.Refund, refund.Id,
            $"refund:{refund.Id:N}:initiated", $"Refund {refund.Reason} for order #{order.Number}",
            await _ledger.BusinessDateAsync(Clock.Now, order.CityId), new[]
            {
                (await _ledger.CompanyAccountAsync(A.CustomerPrepaid), refund.Amount, 0L, (Guid?)order.Id),
                (await _ledger.CompanyAccountAsync(A.RefundPayable), 0L, refund.Amount, order.Id)
            });
    }

    /* J-REFUND-SETTLED — Dr REFUND_PAYABLE / Cr WALLET_CLEARING (the provider paid it back). */
    public async Task PostRefundSettledAsync(Refund refund, Order order)
    {
        await _ledger.PostAsync(JournalEntryType.RefundSettled, JournalSourceType.Refund, refund.Id,
            $"refund:{refund.Id:N}:settled", $"Refund paid by the wallet provider, order #{order.Number}",
            await _ledger.BusinessDateAsync(Clock.Now, order.CityId), new[]
            {
                (await _ledger.CompanyAccountAsync(A.RefundPayable), refund.Amount, 0L, (Guid?)order.Id),
                (await _ledger.CompanyAccountAsync(A.WalletClearing), 0L, refund.Amount, order.Id)
            });
    }

    /* J-REFUND-MANUAL — Dr REFUND_PAYABLE / Cr COMPANY_BANK or CASH_ON_HAND (returned outside the system). */
    public async Task PostRefundManualAsync(Refund refund, Order order, ManualPaymentChannel channel, Guid accountantId)
    {
        await _ledger.PostAsync(JournalEntryType.RefundManual, JournalSourceType.Refund, refund.Id,
            $"refund:{refund.Id:N}:manual", $"Refund returned by hand ({channel}), order #{order.Number}",
            await _ledger.BusinessDateAsync(Clock.Now, order.CityId), new[]
            {
                (await _ledger.CompanyAccountAsync(A.RefundPayable), refund.Amount, 0L, (Guid?)order.Id),
                (await _ledger.CompanyAccountAsync(channel == ManualPaymentChannel.Cash ? A.CashOnHand : A.CompanyBank),
                    0L, refund.Amount, order.Id)
            }, accountantId);
    }

    /* Money of this order still held as prepaid: captured − consumed − already being refunded. */
    public async Task<long> PrepaidRemainingAsync(Guid orderId)
    {
        var prepaid = await _ledger.CompanyAccountAsync(A.CustomerPrepaid);
        var lines = await _lineRepository.GetQueryableAsync();
        return await AsyncExecuter.SumAsync(lines.Where(l => l.AccountId == prepaid && l.OrderId == orderId), l => l.Credit - l.Debit);
    }
}
