using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Saham.Entities.Orders;
using Saham.Entities.Pricing;
using Volo.Abp.Application.Dtos;

namespace Saham.Services.Dtos.Orders;

// ------------------------------------------------------------------ inputs

/* POST /orders (SRS 15): the quote id plus how the customer pays — never an amount (BR-013). */
public class CreateOrderDto
{
    [Required]
    public Guid QuoteId { get; set; }

    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    /* Optional "which banknote will you pay with?" (DER-CUS-05). */
    [Range(0, long.MaxValue)]
    public long? CashTendered { get; set; }

    /* A different recipient for this order only (REQ-CUS-18); defaults to the address' recipient. */
    [StringLength(SahamConsts.NameMaxLength)]
    public string? RecipientName { get; set; }

    [StringLength(SahamConsts.PhoneNumberMaxLength + 5)]
    public string? RecipientPhone { get; set; }
}

public class AcceptOrderDto
{
    /* Preparation time in minutes, 5..120 (BR-024). */
    public int PrepMinutes { get; set; }
}

public class RejectOrderDto
{
    public RestaurantRejectReason Reason { get; set; }

    [StringLength(256)]
    public string? Note { get; set; }
}

public class CancelOrderDto
{
    [Required]
    [StringLength(512)]
    public string Note { get; set; } = string.Empty;
}

public class ReportItemsUnavailableDto
{
    [Required]
    [MinLength(1)]
    public List<Guid> OrderItemIds { get; set; } = new();
}

public class ItemDecisionDto
{
    public ItemDecision Decision { get; set; }
}

public class GetCustomerOrdersInput : PagedResultRequestDto
{
}

public class GetRestaurantOrdersInput : PagedResultRequestDto
{
    /* true (default): orders still in progress at the restaurant. */
    public bool ActiveOnly { get; set; } = true;

    public OrderStatus? Status { get; set; }
}

public class GetOpsOrdersInput : PagedResultRequestDto
{
    public bool ActiveOnly { get; set; } = true;

    public OrderStatus? Status { get; set; }

    public Guid? RestaurantId { get; set; }

    public long? Number { get; set; }
}

// ------------------------------------------------------------------ outputs

public class OrderLineDto : EntityDto<Guid>
{
    public Guid MenuItemId { get; set; }

    public string Name { get; set; } = string.Empty;

    public long UnitPrice { get; set; }

    public int Quantity { get; set; }

    public long LineTotal { get; set; }

    public OrderItemStatus Status { get; set; }

    public bool IsReportedUnavailable { get; set; }
}

/* The customer's view of their order (REQ-CUS-10, DER-CUS-09). */
public class CustomerOrderDto : EntityDto<Guid>
{
    public long Number { get; set; }

    public OrderStatus Status { get; set; }

    public OrderHoldReason? HoldReason { get; set; }

    public Guid RestaurantId { get; set; }

    public string RestaurantName { get; set; } = string.Empty;

    public OrderType OrderType { get; set; }

    public DateTime? ScheduledFor { get; set; }

    public string DeliveryDescription { get; set; } = string.Empty;

    public string? RecipientName { get; set; }

    public List<OrderLineDto> Items { get; set; } = new();

    public long ItemsSubtotal { get; set; }

    public long DiscountAmount { get; set; }

    public bool FreeDeliveryApplied { get; set; }

    public long DeliveryFee { get; set; }

    public long TotalAmount { get; set; }

    public long DebtCollected { get; set; }

    public long CreditApplied { get; set; }

    public long AmountDue { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public long? CashTendered { get; set; }

    /* Change the driver must give back (shown before acceptance, DER-CUS-05). */
    public long? ChangeDue { get; set; }

    public DateTime CreationTime { get; set; }

    public DateTime? AcceptedAt { get; set; }

    public DateTime? ReadyAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public DateTime EtaMinAt { get; set; }

    public DateTime EtaMaxAt { get; set; }

    public CancelReason? CancelReason { get; set; }

    /* Set while the customer must choose for unavailable items (REQ-CUS-21). */
    public DateTime? ItemDecisionDeadlineAt { get; set; }

    /* Free cancellation from the app is possible (BR-045). */
    public bool CanCancel { get; set; }
}

/* The restaurant tablet's view (REQ-RST-01/05). No customer phone or full address (DER-RST-09). */
public class RestaurantOrderDto : EntityDto<Guid>
{
    public long Number { get; set; }

    public OrderStatus Status { get; set; }

    public OrderHoldReason? HoldReason { get; set; }

    public string CustomerFirstName { get; set; } = string.Empty;

    public OrderType OrderType { get; set; }

    public DateTime? ScheduledFor { get; set; }

    public List<OrderLineDto> Items { get; set; } = new();

    public long ItemsSubtotal { get; set; }

    public long RestaurantDiscount { get; set; }

    public long CommissionAmount { get; set; }

    public long RestaurantNetAmount { get; set; }

    public int? PrepMinutes { get; set; }

    public DateTime? PlacedAt { get; set; }

    public DateTime? AcceptedAt { get; set; }

    public DateTime? EstimatedReadyAt { get; set; }

    public DateTime? ReadyAt { get; set; }

    public CancelReason? CancelReason { get; set; }

    public string? CancelNote { get; set; }

    public DateTime? ItemDecisionDeadlineAt { get; set; }
}

public class ReportItemsUnavailableResultDto
{
    public RestaurantOrderDto Order { get; set; } = new();

    /* Menu items the kitchen can now switch off with one tap (DER-RST-06). */
    public List<Guid> MenuItemIdsToSwitchOff { get; set; } = new();
}

public class OrderHistoryDto
{
    public OrderEventType EventType { get; set; }

    public OrderStatus? FromStatus { get; set; }

    public OrderStatus? ToStatus { get; set; }

    public ActorType ActorType { get; set; }

    public Guid? ActorUserId { get; set; }

    public string? Reason { get; set; }

    public DateTime OccurredAt { get; set; }
}

/* Ops / owner view: everything about the order, including its history. */
public class OpsOrderDto : EntityDto<Guid>
{
    public long Number { get; set; }

    public OrderStatus Status { get; set; }

    public OrderHoldReason? HoldReason { get; set; }

    public DispatchStatus DispatchStatus { get; set; }

    public Guid RestaurantId { get; set; }

    public string RestaurantName { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public string? CustomerName { get; set; }

    public OrderType OrderType { get; set; }

    public DateTime? ScheduledFor { get; set; }

    public DateTime? ReleaseAt { get; set; }

    public string DeliveryDescription { get; set; } = string.Empty;

    public string? RecipientName { get; set; }

    public int DistanceM { get; set; }

    public List<OrderLineDto> Items { get; set; } = new();

    public long ItemsSubtotal { get; set; }

    public long DiscountAmount { get; set; }

    public long DeliveryFee { get; set; }

    public long TotalAmount { get; set; }

    public long CommissionAmount { get; set; }

    public long RestaurantNetAmount { get; set; }

    public long DriverPayAmount { get; set; }

    public long AmountDue { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public long? CashTendered { get; set; }

    public DateTime CreationTime { get; set; }

    public DateTime? PlacedAt { get; set; }

    public DateTime? AcceptedAt { get; set; }

    public DateTime? EstimatedReadyAt { get; set; }

    public DateTime EtaMaxAt { get; set; }

    public CancelReason? CancelReason { get; set; }

    public string? CancelNote { get; set; }

    public ActorType? CancelledByType { get; set; }

    public DateTime? ItemDecisionDeadlineAt { get; set; }

    public List<OrderHistoryDto> History { get; set; } = new();
}

/* The customer's tracking screen (REQ-CUS-10/11/13): the driver's position only between
 * assignment and delivery (BR-036), and the delivery code once the order has left the restaurant. */
public class OrderTrackingDto
{
    public Guid OrderId { get; set; }

    public long Number { get; set; }

    public OrderStatus Status { get; set; }

    public OrderHoldReason? HoldReason { get; set; }

    public bool DriverAssigned { get; set; }

    public string? DriverFirstName { get; set; }

    public Saham.Services.Dtos.Shared.LocationDto? DriverLocation { get; set; }

    public DateTime? DriverLocationAt { get; set; }

    /* Shown to the customer only; give it to the driver after receiving the order (EX-13). */
    public string? DeliveryCode { get; set; }

    public DateTime? PickedUpAt { get; set; }

    public DateTime? DeliveredAt { get; set; }

    public DateTime EtaMinAt { get; set; }

    public DateTime EtaMaxAt { get; set; }
}
