using System;
using System.Linq;
using System.Threading.Tasks;
using Saham.Entities.Dispatch;
using Saham.Entities.Drivers;
using Saham.Entities.Orders;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus;

namespace Saham.Entities.Ledger;

/* Posts the money of an order in the same transaction as its final status (SRS 14.6:
 * "delivery: order, assignment, counter, cash payment, revenue and pay entries — one transaction"):
 *   Delivered (cash)              → J-CASH-DELIVERED + J-DRIVER-PAY (+ J-CHANGE-SHORTFALL), cap check;
 *   DeliveryFailed / no-show      → J-NOT-RECEIVED + J-DRIVER-PAY;
 *   DeliveryFailed / driver lost  → J-DRIVER-INCIDENT (no pay).
 * Every entry has its idempotency key, so a replayed event never posts twice (INV-03, INV-09). */
public class LedgerOrderEventHandler : ILocalEventHandler<OrderChangedEto>, ITransientDependency
{
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IRepository<DeliveryAssignment, Guid> _assignmentRepository;
    private readonly IRepository<Driver, Guid> _driverRepository;
    private readonly OrderPostingService _posting;
    private readonly CashManager _cashManager;

    public LedgerOrderEventHandler(
        IRepository<Order, Guid> orderRepository,
        IRepository<DeliveryAssignment, Guid> assignmentRepository,
        IRepository<Driver, Guid> driverRepository,
        OrderPostingService posting,
        CashManager cashManager)
    {
        _orderRepository = orderRepository;
        _assignmentRepository = assignmentRepository;
        _driverRepository = driverRepository;
        _posting = posting;
        _cashManager = cashManager;
    }

    public async Task HandleEventAsync(OrderChangedEto e)
    {
        if (e.EventType != OrderEventType.Transition ||
            e.Status is not (OrderStatus.Delivered or OrderStatus.DeliveryFailed))
        {
            return;
        }

        var order = await _orderRepository.GetAsync(e.OrderId);
        var assignment = (await _assignmentRepository.GetListAsync(a => a.OrderId == order.Id &&
                (a.Status == AssignmentStatus.Delivered || a.Status == AssignmentStatus.Failed)))
            .OrderByDescending(a => a.AssignedAt).FirstOrDefault();

        if (order.Status == OrderStatus.Delivered)
        {
            if (order.PaymentMethod != PaymentMethod.Cash || assignment == null)
            {
                return; // wallet orders are posted with the payment provider (phase 9)
            }

            await _posting.PostCashDeliveredAsync(order, assignment.DriverId);
            await _posting.PostChangeShortfallAsync(order, assignment.DriverId);
            await _posting.PostDriverPayAsync(order, assignment.DriverId, assignment.DriverPayAmount);
            await _cashManager.CheckCapAsync(await _driverRepository.GetAsync(assignment.DriverId));
            return;
        }

        switch (order.FailureReason)
        {
            case FailureReason.CustomerNoShow:
                await _posting.PostNotReceivedAsync(order);
                if (assignment != null)
                {
                    await _posting.PostDriverPayAsync(order, assignment.DriverId, assignment.DriverPayAmount);
                }
                break;

            case FailureReason.DriverIncident:
                await _posting.PostDriverIncidentAsync(order);
                break;

            // QUALITY_REFUSAL / ADDRESS_NOT_FOUND: who pays is decided case by case through a complaint
            // and an approved adjustment (BR-063 §2), not automatically.
        }
    }
}
