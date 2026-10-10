namespace Saham.Entities.Orders;

/* orders.status — the work/preparation dimension only (SRS 10.1).
 * Dispatch, payment and refunds have their own status fields so impossible combinations
 * ("ready before a driver exists", "assigned before ready") cannot be expressed. */
public enum OrderStatus
{
    PendingPayment = 0,
    Scheduled = 1,
    Placed = 2,
    Accepted = 3,
    Ready = 4,
    PickedUp = 5,
    Delivered = 6,
    Cancelled = 7,
    DeliveryFailed = 8
}

/* orders.hold_reason: while set, "ready", pickup and dispatch are blocked (BR-047). */
public enum OrderHoldReason
{
    ItemUnavailable = 0
}

/* orders.dispatch_status (driven by the dispatch engine in phase 5). */
public enum DispatchStatus
{
    NotStarted = 0,
    Searching = 1,
    Assigned = 2,
    Paused = 3,
    Stopped = 4
}

public enum PaymentMethod
{
    Cash = 0,
    Wallet = 1
}

/* SRS 10.3: cancellation reason codes. */
public enum CancelReason
{
    CustomerCancelled = 0,
    RestaurantRejected = 1,
    RestaurantCancelled = 2,
    RestaurantTimeout = 3,
    ItemUnavailable = 4,
    PaymentFailed = 5,
    PaymentTimeout = 6,
    OpsCancelled = 7,
    ZoneClosed = 8,
    RestaurantClosedAtRelease = 9
}

/* SRS 10.3: delivery failure reason codes (used from phase 6). */
public enum FailureReason
{
    CustomerNoShow = 0,
    QualityRefusal = 1,
    AddressNotFound = 2,
    DriverIncident = 3
}

/* REQ-RST-04: rejection reasons offered to the restaurant. */
public enum RestaurantRejectReason
{
    ItemUnavailable = 0,
    Busy = 1,
    Closed = 2,
    Other = 3
}

public enum ActorType
{
    Customer = 0,
    Restaurant = 1,
    Driver = 2,
    Staff = 3,
    System = 4,
    Provider = 5
}

/* order_status_history.event_type */
public enum OrderEventType
{
    Transition = 0,
    Hold = 1,
    Unhold = 2,
    Modified = 3,
    Assigned = 4,
    Released = 5,
    PrepExtended = 6,
    ItemDecision = 7,
    ArrivedAtRestaurant = 8,
    HandedOver = 9
}

public enum OrderItemStatus
{
    Active = 0,
    Removed = 1,
    ProposedSubstitute = 2
}

/* Customer's answer when an item runs out after acceptance (MVP: two options, Appendix C §5). */
public enum ItemDecision
{
    ContinueWithout = 0,
    CancelOrder = 1
}

public enum OrderModificationType
{
    ItemsRemoved = 0,
    AddressChanged = 1
}
