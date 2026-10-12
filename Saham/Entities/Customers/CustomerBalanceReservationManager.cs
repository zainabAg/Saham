using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Saham.Entities.Orders;
using Saham.Entities.Support;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Saham.Entities.Customers;

/* Customer credit and debt promised to open orders (BR-064, BR-071), on top of the ledger:
 *   quote (preview)  → PlanAsync without the lock; nothing is held;
 *   order creation   → lock, PlanAsync again, ReserveAsync with the order, one transaction;
 *   ledger posting   → ConsumeAsync when the delivery entry posts the same credit/debt,
 *                      ReleaseAsync when the order ends without posting them (LedgerOrderEventHandler);
 *   modification     → SyncWithOrderAsync: the hold follows the order's approved amounts (only down).
 * The ledger stays the source of the confirmed balance; the provider returns it raw and only
 * CustomerBalanceReservationCalculator subtracts the holds, so nothing is deducted twice.
 *
 * Over-commitment policy: a posting from another order (a "not received" debt, a late-cancel charge,
 * change owed) may leave active holds above the matching posted amount. Frozen order amounts are NOT
 * changed: the cash driver collects, and the wallet captured, exactly what the customer agreed to, and
 * the ledger settles the difference in the same account when those orders post. Until then new
 * allocations fail closed (CustomerBalanceReservationConflict) and a CUSTOMER_BALANCE_OVERCOMMITTED
 * incident is opened for the accountant; it closes itself once the state is consistent again. */
public class CustomerBalanceReservationManager : DomainService
{
    private readonly IRepository<CustomerBalanceReservation, Guid> _reservationRepository;
    private readonly ICustomerBalanceProvider _balanceProvider;
    private readonly CustomerBalanceLock _lock;
    private readonly IncidentManager _incidentManager;

    public CustomerBalanceReservationManager(
        IRepository<CustomerBalanceReservation, Guid> reservationRepository,
        ICustomerBalanceProvider balanceProvider,
        CustomerBalanceLock balanceLock,
        IncidentManager incidentManager)
    {
        _reservationRepository = reservationRepository;
        _balanceProvider = balanceProvider;
        _lock = balanceLock;
        _incidentManager = incidentManager;
    }

    /* DebtCollected, CreditApplied and AmountDue for a new order of this total. Binding only when the
     * caller holds the lock and saves the order in the same transaction; otherwise a preview. */
    public async Task<CustomerBalanceReservationPlan> PlanAsync(Guid customerId, long orderTotal)
    {
        var posted = await _balanceProvider.GetBalanceAsync(customerId);
        var holds = await GetActiveSnapshotsAsync(customerId);
        try
        {
            return CustomerBalanceReservationCalculator.Calculate(orderTotal, posted, holds);
        }
        catch (CustomerBalanceReservationConflictException)
        {
            Logger.LogWarning("Customer {CustomerId}: active holds exceed the posted balance {Balance}; allocation refused.",
                customerId, posted);
            throw new BusinessException(SahamErrorCodes.CustomerBalanceReservationConflict);
        }
    }

    /* The order's frozen amounts, held until it posts or ends. Zero amounts hold nothing. */
    public async Task ReserveAsync(Order order)
    {
        _lock.EnsureHeld(order.CustomerId);
        if (order.CreditApplied == 0 && order.DebtCollected == 0)
        {
            return;
        }

        await _reservationRepository.InsertAsync(new CustomerBalanceReservation(
            GuidGenerator.Create(), order.CustomerId, order.Id, order.CreditApplied, order.DebtCollected), autoSave: true);
    }

    /* The order's ledger entry posted exactly these amounts to CUSTOMER_BALANCE (same transaction). */
    public Task ConsumeAsync(Order order, string reason) => FinalizeAsync(order, consume: true, reason);

    /* The order ended and its credit/debt were not posted: they are free for the next order. */
    public Task ReleaseAsync(Order order, string reason) => FinalizeAsync(order, consume: false, reason);

    /* BR-048 (items removed / replaced): Order.ChangeItems keeps the debt and lowers the credit to what is
     * still owed. The hold follows; it never grows here, because no approved path raises it. */
    public async Task SyncWithOrderAsync(Order order)
    {
        _lock.EnsureHeld(order.CustomerId);
        var reservation = await FindAsync(order.Id);
        if (reservation == null)
        {
            if (order.CreditApplied == 0 && order.DebtCollected == 0)
            {
                return;
            }

            Logger.LogError("Order {OrderId} uses credit {Credit} / debt {Debt} without a reservation.",
                order.Id, order.CreditApplied, order.DebtCollected);
            throw new BusinessException(SahamErrorCodes.CustomerBalanceReservationConflict).WithData("orderId", order.Id);
        }

        if (reservation.CreditApplied == order.CreditApplied && reservation.DebtCollected == order.DebtCollected)
        {
            return;
        }

        Logger.LogInformation("Reservation of order {OrderId} adjusted: credit {OldCredit} → {Credit}, debt {OldDebt} → {Debt}.",
            order.Id, reservation.CreditApplied, order.CreditApplied, reservation.DebtCollected, order.DebtCollected);
        reservation.AdjustTo(order.CreditApplied, order.DebtCollected); // the order's own audit: OrderModification
        await _reservationRepository.UpdateAsync(reservation, autoSave: true);
    }

    /* After any posting on CUSTOMER_BALANCE: opens or closes the over-commitment incident (see above). */
    public async Task CheckConsistencyAsync(Guid customerId, Guid? orderId)
    {
        _lock.EnsureHeld(customerId);
        var posted = await _balanceProvider.GetBalanceAsync(customerId);
        var holds = await GetActiveSnapshotsAsync(customerId);
        try
        {
            CustomerBalanceReservationCalculator.Calculate(0, posted, holds);
        }
        catch (CustomerBalanceReservationConflictException)
        {
            Logger.LogWarning("Customer {CustomerId}: {Count} active holds (credit {Credit}, debt {Debt}) exceed the posted balance {Balance}.",
                customerId, holds.Count, holds.Sum(h => h.CreditApplied), holds.Sum(h => h.DebtCollected), posted);
            await _incidentManager.OpenAsync(IncidentType.CustomerBalanceOvercommitted, IncidentSeverity.High, customerId, orderId: orderId);
            return;
        }

        await _incidentManager.AutoResolveAsync(IncidentType.CustomerBalanceOvercommitted, customerId, "CONSISTENT");
    }

    private async Task FinalizeAsync(Order order, bool consume, string reason)
    {
        _lock.EnsureHeld(order.CustomerId);
        var reservation = await FindAsync(order.Id);
        if (reservation == null)
        {
            return; // nothing was held (zero amounts)
        }

        var changed = consume ? reservation.Consume(reason, Clock.Now) : reservation.Release(reason, Clock.Now);
        if (changed)
        {
            await _reservationRepository.UpdateAsync(reservation, autoSave: true);
        }
    }

    /* Fresh from the database (a projection is not served from EF's tracked copies); every write above
     * is saved at once so this sees it within the transaction. */
    private async Task<List<CustomerBalanceReservationSnapshot>> GetActiveSnapshotsAsync(Guid customerId)
    {
        var queryable = await _reservationRepository.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(queryable
            .Where(r => r.CustomerId == customerId && r.Status == CustomerBalanceReservationStatus.Active)
            .Select(r => new CustomerBalanceReservationSnapshot(r.OrderId, r.CreditApplied, r.DebtCollected)));
    }

    private Task<CustomerBalanceReservation?> FindAsync(Guid orderId) =>
        _reservationRepository.FindAsync(r => r.OrderId == orderId);
}
