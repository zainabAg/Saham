using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Saham.Entities.Dispatch;
using Saham.Entities.Orders;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus;
using Volo.Abp.Guids;

namespace Saham.Entities.Notifications;

/* Turns order changes into outbox events in the same transaction (SRS I-6). Only the events of the
 * notification matrix (I.2-6) are written; everything else is realtime only. */
public class OrderOutboxEventHandler : ILocalEventHandler<OrderChangedEto>, ITransientDependency
{
    private readonly OutboxManager _outbox;
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IGuidGenerator _guidGenerator;

    public OrderOutboxEventHandler(OutboxManager outbox, IRepository<Order, Guid> orderRepository, IGuidGenerator guidGenerator)
    {
        _outbox = outbox;
        _orderRepository = orderRepository;
        _guidGenerator = guidGenerator;
    }

    public async Task HandleEventAsync(OrderChangedEto e)
    {
        var (eventType, repeatable) = await ResolveAsync(e);
        if (eventType == null)
        {
            return;
        }

        await _outbox.EnqueueAsync(eventType, OutboxAggregateTypes.Order, e.OrderId,
            new Dictionary<string, string?> { ["orderId"] = e.OrderId.ToString() },
            repeatable ? _guidGenerator.Create().ToString("N") : null);
    }

    private async Task<(string? EventType, bool Repeatable)> ResolveAsync(OrderChangedEto e)
    {
        switch (e.EventType)
        {
            case OrderEventType.Hold when e.HoldReason == OrderHoldReason.ItemUnavailable:
                return (OutboxEventTypes.ItemUnavailable, true);
            case OrderEventType.Assigned:
                return (OutboxEventTypes.DriverAssigned, true);
            case OrderEventType.Transition:
                break;
            default:
                return (null, false);
        }

        switch (e.Status)
        {
            case OrderStatus.Placed:
                return (OutboxEventTypes.OrderPlaced, false);
            case OrderStatus.Accepted:
                return (OutboxEventTypes.OrderAccepted, false);
            case OrderStatus.Ready:
                return (OutboxEventTypes.OrderReady, false);
            case OrderStatus.PickedUp:
                return (OutboxEventTypes.OrderPickedUp, false);
            case OrderStatus.Delivered:
                return (OutboxEventTypes.OrderDelivered, false);
            case OrderStatus.Cancelled:
                var cancelled = await _orderRepository.GetAsync(e.OrderId, includeDetails: false);
                if (cancelled.CancelledByType == ActorType.Customer)
                {
                    return (null, false); // the customer cancelled it themselves
                }

                return (cancelled.CancelReason == CancelReason.RestaurantRejected
                    ? OutboxEventTypes.OrderRejected
                    : OutboxEventTypes.OrderCancelled, false);
            case OrderStatus.DeliveryFailed:
                var failed = await _orderRepository.GetAsync(e.OrderId, includeDetails: false);
                return (failed.FailureReason == FailureReason.CustomerNoShow ? OutboxEventTypes.OrderNotReceived : null, false);
            default:
                return (null, false);
        }
    }
}

/* Driver app events that must reach a phone whose app is closed: a new offer (high priority),
 * a task given or taken away (incl. the order being cancelled). Offer cancellations are realtime only. */
public class DriverOutboxEventHandler : ILocalEventHandler<DriverNotificationEto>, ITransientDependency
{
    private readonly OutboxManager _outbox;
    private readonly IGuidGenerator _guidGenerator;

    public DriverOutboxEventHandler(OutboxManager outbox, IGuidGenerator guidGenerator)
    {
        _outbox = outbox;
        _guidGenerator = guidGenerator;
    }

    public async Task HandleEventAsync(DriverNotificationEto e)
    {
        var payload = new Dictionary<string, string?>
        {
            ["driverId"] = e.DriverId.ToString(),
            ["orderId"] = e.OrderId.ToString(),
            ["offerId"] = e.OfferId?.ToString(),
            ["expiresAt"] = e.ExpiresAt?.ToString("O")
        };

        switch (e.Kind)
        {
            case DriverNotificationKind.OfferCreated:
                await _outbox.EnqueueAsync(OutboxEventTypes.DeliveryOffer, OutboxAggregateTypes.Driver, e.DriverId, payload,
                    e.OfferId?.ToString("N"));
                break;
            case DriverNotificationKind.TaskAssigned:
                await _outbox.EnqueueAsync(OutboxEventTypes.TaskAssigned, OutboxAggregateTypes.Driver, e.DriverId, payload,
                    _guidGenerator.Create().ToString("N"));
                break;
            case DriverNotificationKind.TaskReleased:
                await _outbox.EnqueueAsync(OutboxEventTypes.TaskReleased, OutboxAggregateTypes.Driver, e.DriverId, payload,
                    _guidGenerator.Create().ToString("N"));
                break;
        }
    }
}
