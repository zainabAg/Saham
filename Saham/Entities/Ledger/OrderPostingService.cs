using System;
using System.Threading.Tasks;
using Saham.Entities.Orders;
using Volo.Abp.Domain.Services;
using A = Saham.Entities.Ledger.LedgerAccountCode;

namespace Saham.Entities.Ledger;

/* Order posting templates (SRS 12.4). Amounts come frozen from the order (BR-014):
 *   S items, K = K_c + K_r discounts, Dfee delivery fee, T total, C commission, R restaurant net,
 *   debt = previous debt collected, credit = in-app credit used, due = T + debt − credit.
 * Revenue is recognised at delivery only (SRS 12.1 §5). */
public class OrderPostingService : DomainService
{
    private readonly LedgerManager _ledger;

    public OrderPostingService(LedgerManager ledger)
    {
        _ledger = ledger;
    }

    /* J-CASH-DELIVERED — the driver collected the cash:
     *   Dr DRIVER_CASH[d] due, Dr CUSTOMER_BALANCE[c] credit, Dr DISCOUNT_EXPENSE K_c
     *   Cr RESTAURANT_PAYABLE[r] R, Cr COMMISSION C, Cr DELIVERY_FEE Dfee, Cr CUSTOMER_BALANCE[c] debt */
    public async Task PostCashDeliveredAsync(Order order, Guid driverId)
    {
        var date = await _ledger.BusinessDateAsync(order.DeliveredAt ?? Clock.Now, order.CityId);
        var customer = await _ledger.PartyAccountAsync(A.CustomerBalance, order.CustomerId);

        await _ledger.PostAsync(JournalEntryType.CashDelivered, JournalSourceType.Order, order.Id,
            $"order:{order.Id:N}:delivered", $"Order #{order.Number} delivered (cash)", date, new[]
            {
                (await _ledger.PartyAccountAsync(A.DriverCash, driverId), order.AmountDue, 0L, (Guid?)order.Id),
                (customer, order.CreditApplied, 0L, order.Id),
                (await _ledger.CompanyAccountAsync(A.DiscountExpense), order.CompanyDiscount, 0L, order.Id),
                (await _ledger.PartyAccountAsync(A.RestaurantPayable, order.RestaurantId), 0L, order.RestaurantNetAmount, order.Id),
                (await _ledger.CompanyAccountAsync(A.CommissionRevenue), 0L, order.CommissionAmount, order.Id),
                (await _ledger.CompanyAccountAsync(A.DeliveryFeeRevenue), 0L, order.DeliveryFeeCharged, order.Id),
                (customer, 0L, order.DebtCollected, order.Id)
            });
    }

    /* J-DRIVER-PAY — Dr DRIVER_PAY_EXPENSE P / Cr DRIVER_PAYABLE[d] P (on delivery or "not received"). */
    public async Task PostDriverPayAsync(Order order, Guid driverId, long pay)
    {
        var date = await _ledger.BusinessDateAsync(Clock.Now, order.CityId);
        await _ledger.PostAsync(JournalEntryType.DriverPay, JournalSourceType.Order, order.Id,
            $"order:{order.Id:N}:driver_pay", $"Driver pay for order #{order.Number}", date, new[]
            {
                (await _ledger.CompanyAccountAsync(A.DriverPayExpense), pay, 0L, (Guid?)order.Id),
                (await _ledger.PartyAccountAsync(A.DriverPayable, driverId), 0L, pay, order.Id)
            });
    }

    /* J-NOT-RECEIVED (cash, DEC-06) — the restaurant and the company are paid in full, the customer owes T:
     *   Dr CUSTOMER_BALANCE[c] T, Dr DISCOUNT_EXPENSE K_c
     *   Cr RESTAURANT_PAYABLE[r] R, Cr COMMISSION C, Cr DELIVERY_FEE Dfee
     * Any in-app credit the customer had absorbs the debt automatically (same account). */
    public async Task PostNotReceivedAsync(Order order)
    {
        var date = await _ledger.BusinessDateAsync(Clock.Now, order.CityId);
        await _ledger.PostAsync(JournalEntryType.NotReceived, JournalSourceType.Order, order.Id,
            $"order:{order.Id:N}:not_received", $"Order #{order.Number} not received", date, new[]
            {
                (await _ledger.PartyAccountAsync(A.CustomerBalance, order.CustomerId), order.TotalAmount, 0L, (Guid?)order.Id),
                (await _ledger.CompanyAccountAsync(A.DiscountExpense), order.CompanyDiscount, 0L, order.Id),
                (await _ledger.PartyAccountAsync(A.RestaurantPayable, order.RestaurantId), 0L, order.RestaurantNetAmount, order.Id),
                (await _ledger.CompanyAccountAsync(A.CommissionRevenue), 0L, order.CommissionAmount, order.Id),
                (await _ledger.CompanyAccountAsync(A.DeliveryFeeRevenue), 0L, order.DeliveryFeeCharged, order.Id)
            });
    }

    /* J-DRIVER-INCIDENT (REC-07) — the driver vanished with the food: the restaurant is not at fault,
     * the company pays its net. Dr ADJUSTMENT_EXPENSE R / Cr RESTAURANT_PAYABLE[r] R. No driver pay. */
    public async Task PostDriverIncidentAsync(Order order)
    {
        var date = await _ledger.BusinessDateAsync(Clock.Now, order.CityId);
        await _ledger.PostAsync(JournalEntryType.DriverIncident, JournalSourceType.Order, order.Id,
            $"order:{order.Id:N}:driver_incident", $"Order #{order.Number} lost by driver incident", date, new[]
            {
                (await _ledger.CompanyAccountAsync(A.AdjustmentExpense), order.RestaurantNetAmount, 0L, (Guid?)order.Id),
                (await _ledger.PartyAccountAsync(A.RestaurantPayable, order.RestaurantId), 0L, order.RestaurantNetAmount, order.Id)
            });
    }

    /* J-CUSTOMER-LATE-CANCEL (BR-045, REC-03) — the customer asked to cancel after acceptance and pays
     * the food S − K: the restaurant keeps its net, the company its commission, no delivery fee.
     *   Dr CUSTOMER_PREPAID (wallet, up to what is held) / CUSTOMER_BALANCE[c] (cash, or the rest: a debt) S − K,
     *   Dr DISCOUNT_EXPENSE K_c;  Cr RESTAURANT_PAYABLE[r] R, Cr COMMISSION C. */
    public async Task PostCustomerLateCancelAsync(Order order, long prepaidAvailable)
    {
        var charge = order.ItemsSubtotal - order.DiscountAmount;
        var fromPrepaid = Math.Min(charge, Math.Max(0, prepaidAvailable));
        var date = await _ledger.BusinessDateAsync(Clock.Now, order.CityId);
        await _ledger.PostAsync(JournalEntryType.CustomerLateCancel, JournalSourceType.Order, order.Id,
            $"order:{order.Id:N}:late_cancel", $"Order #{order.Number} cancelled late by the customer", date, new[]
            {
                (await _ledger.CompanyAccountAsync(A.CustomerPrepaid), fromPrepaid, 0L, (Guid?)order.Id),
                (await _ledger.PartyAccountAsync(A.CustomerBalance, order.CustomerId), charge - fromPrepaid, 0L, order.Id),
                (await _ledger.CompanyAccountAsync(A.DiscountExpense), order.CompanyDiscount, 0L, order.Id),
                (await _ledger.PartyAccountAsync(A.RestaurantPayable, order.RestaurantId), 0L, order.RestaurantNetAmount, order.Id),
                (await _ledger.CompanyAccountAsync(A.CommissionRevenue), 0L, order.CommissionAmount, order.Id)
            });
    }

    /* J-CANCEL-DRIVER-PAY (BR-069, DEC-08) — the driver reached the restaurant before the cancellation and
     * gets part of his pay, charged to whoever caused it:
     *   restaurant → Dr RESTAURANT_PAYABLE[r]; customer → Dr CUSTOMER_PREPAID (wallet) / CUSTOMER_BALANCE[c];
     *   company → Dr DRIVER_PAY_EXPENSE;  Cr DRIVER_PAYABLE[d]. */
    public async Task PostCancelDriverPayAsync(Order order, Guid driverId, long pay, CancelCause cause, long prepaidAvailable)
    {
        if (pay <= 0)
        {
            return;
        }

        var lines = new System.Collections.Generic.List<(Guid, long, long, Guid?)>();
        switch (cause)
        {
            case CancelCause.Restaurant:
                lines.Add((await _ledger.PartyAccountAsync(A.RestaurantPayable, order.RestaurantId), pay, 0L, order.Id));
                break;
            case CancelCause.Customer:
                var fromPrepaid = Math.Min(pay, Math.Max(0, prepaidAvailable));
                lines.Add((await _ledger.CompanyAccountAsync(A.CustomerPrepaid), fromPrepaid, 0L, order.Id));
                lines.Add((await _ledger.PartyAccountAsync(A.CustomerBalance, order.CustomerId), pay - fromPrepaid, 0L, order.Id));
                break;
            default:
                lines.Add((await _ledger.CompanyAccountAsync(A.DriverPayExpense), pay, 0L, order.Id));
                break;
        }

        lines.Add((await _ledger.PartyAccountAsync(A.DriverPayable, driverId), 0L, pay, order.Id));
        await _ledger.PostAsync(JournalEntryType.CancelDriverPay, JournalSourceType.Order, order.Id,
            $"order:{order.Id:N}:cancel_pay", $"Driver pay for cancelled order #{order.Number} ({cause})",
            await _ledger.BusinessDateAsync(Clock.Now, order.CityId), lines);
    }

    /* J-CHANGE-SHORTFALL (EX-14) — the driver kept X of the customer's change:
     * Dr DRIVER_CASH[d] X / Cr CUSTOMER_BALANCE[c] X (credit used on the next order). */
    public async Task PostChangeShortfallAsync(Order order, Guid driverId)
    {
        if (order.ChangeShortfall <= 0)
        {
            return;
        }

        var date = await _ledger.BusinessDateAsync(Clock.Now, order.CityId);
        await _ledger.PostAsync(JournalEntryType.ChangeShortfall, JournalSourceType.Order, order.Id,
            $"order:{order.Id:N}:change_shortfall", $"Change owed to customer, order #{order.Number}", date, new[]
            {
                (await _ledger.PartyAccountAsync(A.DriverCash, driverId), order.ChangeShortfall, 0L, (Guid?)order.Id),
                (await _ledger.PartyAccountAsync(A.CustomerBalance, order.CustomerId), 0L, order.ChangeShortfall, order.Id)
            });
    }
}
