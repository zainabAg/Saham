using System;
using Volo.Abp.Domain.Entities;

namespace Saham.Entities.Orders;

/* Append-only log of every transition and notable event of an order (SRS order_status_history). */
public class OrderStatusHistory : Entity<Guid>
{
    public Guid OrderId { get; private set; }

    public OrderEventType EventType { get; private set; }

    public OrderStatus? FromStatus { get; private set; }

    public OrderStatus? ToStatus { get; private set; }

    public ActorType ActorType { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string? Reason { get; private set; }

    public DateTime OccurredAt { get; private set; }

    /* Device time of an offline driver action (EX-01), when it differs from OccurredAt. */
    public DateTime? ClientOccurredAt { get; private set; }

    protected OrderStatusHistory()
    {
    }

    internal OrderStatusHistory(
        Guid id,
        Guid orderId,
        OrderEventType eventType,
        OrderStatus? fromStatus,
        OrderStatus? toStatus,
        ActorType actorType,
        Guid? actorUserId,
        string? reason,
        DateTime occurredAt,
        DateTime? clientOccurredAt = null)
        : base(id)
    {
        OrderId = orderId;
        EventType = eventType;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ActorType = actorType;
        ActorUserId = actorUserId;
        Reason = reason;
        OccurredAt = occurredAt;
        ClientOccurredAt = clientOccurredAt;
    }
}
