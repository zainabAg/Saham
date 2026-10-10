using System.Collections.Generic;
using Volo.Abp;

namespace Saham.Entities.Orders;

/* The single table of allowed order transitions (SRS 10.4, T-01..T-17).
 * Defined once and checked on every status change; anything else is
 * 409 INVALID_STATE_TRANSITION (SRS 10.7). */
public static class OrderStateMachine
{
    private static readonly HashSet<(OrderStatus From, OrderStatus To)> Allowed = new()
    {
        (OrderStatus.PendingPayment, OrderStatus.Placed),     // T-04 wallet captured, ASAP
        (OrderStatus.PendingPayment, OrderStatus.Scheduled),  // T-05 wallet captured, scheduled
        (OrderStatus.PendingPayment, OrderStatus.Cancelled),  // T-06 payment failed / timeout / customer
        (OrderStatus.Scheduled, OrderStatus.Placed),          // T-07 release time
        (OrderStatus.Scheduled, OrderStatus.Cancelled),       // T-08 customer or ops
        (OrderStatus.Placed, OrderStatus.Accepted),           // T-09 restaurant accepts with prep time
        (OrderStatus.Placed, OrderStatus.Cancelled),          // T-10 reject, timeout, customer, ops
        (OrderStatus.Accepted, OrderStatus.Ready),            // T-11 restaurant ready
        (OrderStatus.Accepted, OrderStatus.PickedUp),         // T-12 pickup, "ready" not marked
        (OrderStatus.Ready, OrderStatus.PickedUp),            // T-13 pickup
        (OrderStatus.Accepted, OrderStatus.Cancelled),        // T-14 restaurant reason, ops, item resolution
        (OrderStatus.Ready, OrderStatus.Cancelled),           // T-15 restaurant reason, ops
        (OrderStatus.PickedUp, OrderStatus.Delivered),        // T-16 OTP, customer confirm, ops override
        (OrderStatus.PickedUp, OrderStatus.DeliveryFailed)    // T-17 ops decision
    };

    public static bool CanTransition(OrderStatus from, OrderStatus to) => Allowed.Contains((from, to));

    public static void EnsureTransition(OrderStatus from, OrderStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new BusinessException(SahamErrorCodes.InvalidStateTransition)
                .WithData("from", from.ToString())
                .WithData("to", to.ToString());
        }
    }

    public static bool IsFinal(OrderStatus status) =>
        status is OrderStatus.Delivered or OrderStatus.Cancelled or OrderStatus.DeliveryFailed;

    /* INV-08: never shown to the restaurant or the dispatch engine. */
    public static bool IsVisibleToRestaurant(OrderStatus status) =>
        status is not (OrderStatus.PendingPayment or OrderStatus.Scheduled);
}
