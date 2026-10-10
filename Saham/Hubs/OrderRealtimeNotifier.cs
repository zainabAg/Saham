using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Saham.Entities.Orders;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus;
using Volo.Abp.Uow;

namespace Saham.Hubs;

/* Pushes order changes to the restaurant, the customer and ops — only after the transaction
 * commits, so nobody is told about a change that was rolled back. Several events of the same
 * order in one transaction are sent once. Delivery here is best effort; durable notifications
 * (push / SMS with retries) come with the outbox in phase 8. */
public class OrderRealtimeNotifier : ILocalEventHandler<OrderChangedEto>, ITransientDependency
{
    public const string MethodName = "orderChanged";
    private const string PendingKey = "Saham.OrderRealtimeNotifier.Pending";

    private readonly IHubContext<OrdersHub> _hubContext;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<OrderRealtimeNotifier> _logger;

    public OrderRealtimeNotifier(
        IHubContext<OrdersHub> hubContext,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<OrderRealtimeNotifier> logger)
    {
        _hubContext = hubContext;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
    }

    public Task HandleEventAsync(OrderChangedEto eventData)
    {
        var uow = _unitOfWorkManager.Current;
        if (uow == null)
        {
            return SendAsync(eventData);
        }

        // Keep only the latest event per order; send once when the unit of work completes.
        var pending = (Dictionary<System.Guid, OrderChangedEto>)uow.Items.GetOrAdd(PendingKey, () =>
        {
            var map = new Dictionary<System.Guid, OrderChangedEto>();
            uow.OnCompleted(async () =>
            {
                foreach (var e in map.Values)
                {
                    await SendAsync(e);
                }
            });
            return map;
        });
        pending[eventData.OrderId] = eventData;
        return Task.CompletedTask;
    }

    private async Task SendAsync(OrderChangedEto e)
    {
        var payload = new
        {
            orderId = e.OrderId,
            status = e.Status.ToString(),
            holdReason = e.HoldReason?.ToString(),
            eventType = e.EventType.ToString()
        };

        try
        {
            await _hubContext.Clients.User(e.CustomerId.ToString()).SendAsync(MethodName, payload);
            await _hubContext.Clients.Group(OrdersHub.OpsGroup).SendAsync(MethodName, payload);
            if (OrderStateMachine.IsVisibleToRestaurant(e.Status)) // INV-08
            {
                await _hubContext.Clients.Group(OrdersHub.RestaurantGroup(e.RestaurantId)).SendAsync(MethodName, payload);
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning(ex, "Realtime notification failed for order {OrderId}", e.OrderId);
        }
    }
}
