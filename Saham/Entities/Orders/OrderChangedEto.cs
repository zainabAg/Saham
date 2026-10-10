using System;

namespace Saham.Entities.Orders;

/* Local event raised by Order on every recorded change (transition, hold, modification). */
public record OrderChangedEto(
    Guid OrderId,
    Guid RestaurantId,
    Guid CustomerId,
    OrderStatus Status,
    OrderHoldReason? HoldReason,
    OrderEventType EventType);
