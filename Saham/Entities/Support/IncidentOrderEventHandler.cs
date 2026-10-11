using System;
using System.Threading.Tasks;
using Saham.Entities.Orders;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus;

namespace Saham.Entities.Support;

/* Order incidents driven by the order's own changes, inside the same transaction:
 *   the restaurant answered (accepted, or the order was cancelled) → RESTAURANT_NO_RESPONSE closes;
 *   the order is finished → its timing incidents close (late, no driver, no answer);
 *   the restaurant cancelled an accepted order → follow-up incident (BR-046);
 *   a scheduled order found the restaurant closed at release → incident (SCHEDULED_RESTAURANT_CLOSED). */
public class IncidentOrderEventHandler : ILocalEventHandler<OrderChangedEto>, ITransientDependency
{
    private static readonly IncidentType[] ClosedWhenFinished =
    {
        IncidentType.RestaurantNoResponse, IncidentType.OrderLate, IncidentType.NoDriver
    };

    private readonly IncidentManager _incidentManager;
    private readonly IRepository<Order, Guid> _orderRepository;

    public IncidentOrderEventHandler(IncidentManager incidentManager, IRepository<Order, Guid> orderRepository)
    {
        _incidentManager = incidentManager;
        _orderRepository = orderRepository;
    }

    public async Task HandleEventAsync(OrderChangedEto e)
    {
        if (e.EventType != OrderEventType.Transition)
        {
            return;
        }

        if (e.Status == OrderStatus.Accepted)
        {
            await _incidentManager.AutoResolveAsync(IncidentType.RestaurantNoResponse, e.OrderId, "ACCEPTED");
            return;
        }

        if (!OrderStateMachine.IsFinal(e.Status))
        {
            return;
        }

        await _incidentManager.AutoResolveForOrderAsync(e.OrderId, ClosedWhenFinished, e.Status.ToString().ToUpperInvariant());

        if (e.Status == OrderStatus.Cancelled)
        {
            var order = await _orderRepository.GetAsync(e.OrderId, includeDetails: false);
            switch (order.CancelReason)
            {
                case CancelReason.RestaurantCancelled:
                    await _incidentManager.OpenOnceAsync(IncidentType.RestaurantCancelledAccepted, IncidentSeverity.Medium,
                        order.Id, orderId: order.Id, restaurantId: order.RestaurantId);
                    break;
                case CancelReason.RestaurantClosedAtRelease:
                    await _incidentManager.OpenOnceAsync(IncidentType.ScheduledRestaurantClosed, IncidentSeverity.High,
                        order.Id, orderId: order.Id, restaurantId: order.RestaurantId);
                    break;
            }
        }
    }
}
