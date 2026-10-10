using System;
using System.Threading.Tasks;
using Saham.Entities.Orders;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus;

namespace Saham.Entities.Dispatch;

/* Keeps dispatch consistent with the order, inside the same transaction (SRS 14.6):
 *   cancelled → pending offers cancelled, a not-yet-picked-up assignment released,
 *               the driver's active count decremented (INV-04, INV-07, BR-046);
 *   on hold   → pending offers cancelled, an existing assignment stays (UC-12). */
public class DispatchOrderEventHandler : ILocalEventHandler<OrderChangedEto>, ITransientDependency
{
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly DispatchManager _dispatchManager;

    public DispatchOrderEventHandler(IRepository<Order, Guid> orderRepository, DispatchManager dispatchManager)
    {
        _orderRepository = orderRepository;
        _dispatchManager = dispatchManager;
    }

    public async Task HandleEventAsync(OrderChangedEto eventData)
    {
        if (eventData.Status == OrderStatus.Cancelled)
        {
            await _dispatchManager.StopForOrderAsync(await _orderRepository.GetAsync(eventData.OrderId));
        }
        else if (eventData.EventType == OrderEventType.Hold)
        {
            await _dispatchManager.PauseForHoldAsync(await _orderRepository.GetAsync(eventData.OrderId));
        }
    }
}
