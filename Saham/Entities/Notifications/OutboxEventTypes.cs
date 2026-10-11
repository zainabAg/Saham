namespace Saham.Entities.Notifications;

/* Event names of the SRS notification matrix (I.2-6). */
public static class OutboxEventTypes
{
    public const string OrderPlaced = "ORDER_PLACED";
    public const string OrderAccepted = "ORDER_ACCEPTED";
    public const string OrderRejected = "ORDER_REJECTED";
    public const string OrderCancelled = "ORDER_CANCELLED";
    public const string OrderReady = "ORDER_READY";
    public const string OrderPickedUp = "ORDER_PICKED_UP";
    public const string OrderDelivered = "ORDER_DELIVERED";
    public const string OrderNotReceived = "ORDER_NOT_RECEIVED";
    public const string OrderLate = "ORDER_LATE";
    public const string ItemUnavailable = "ITEM_UNAVAILABLE";
    public const string DriverAssigned = "DRIVER_ASSIGNED";
    public const string DeliveryOffer = "DELIVERY_OFFER";
    public const string TaskAssigned = "TASK_ASSIGNED";
    public const string TaskReleased = "TASK_RELEASED";
    public const string CustomerCashBanned = "CUSTOMER_CASH_BANNED";
    public const string DriverCashOverdue = "DRIVER_CASH_OVERDUE";
    public const string DriverReviewed = "DRIVER_REVIEWED";
    public const string RefundSucceeded = "REFUND_SUCCEEDED";
    public const string RefundFailedFinal = "REFUND_FAILED_FINAL";
    public const string DriverPayoutPaid = "DRIVER_PAYOUT_PAID";
}

public static class OutboxAggregateTypes
{
    public const string Order = "Order";
    public const string Driver = "Driver";
    public const string Customer = "Customer";
}
