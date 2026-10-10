using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Saham.Entities.Pricing;
using Volo.Abp.Application.Dtos;

namespace Saham.Services.Dtos.Customers;

/* POST /quotes (SRS 15): only ids and quantities — the client never sends a price (BR-013). */
public class CreateQuoteDto
{
    [Required]
    public Guid RestaurantId { get; set; }

    [Required]
    public Guid AddressId { get; set; }

    [Required]
    [MinLength(1)]
    public List<QuoteItemInput> Items { get; set; } = new();

    public OrderType OrderType { get; set; } = OrderType.Asap;

    /* UTC; required for scheduled orders. */
    public DateTime? ScheduledFor { get; set; }
}

public class QuoteItemInput
{
    [Required]
    public Guid MenuItemId { get; set; }

    [Range(1, QuoteManager.MaxQuantityPerLine)]
    public int Quantity { get; set; }
}

/* What the customer sees before confirming (REQ-CUS-03). Internal amounts
 * (commission, restaurant net, driver pay) stay on the server. */
public class QuoteDto : EntityDto<Guid>
{
    public DateTime ExpiresAt { get; set; }

    public Guid RestaurantId { get; set; }

    public Guid AddressId { get; set; }

    public OrderType OrderType { get; set; }

    public DateTime? ScheduledFor { get; set; }

    public List<QuoteLineDto> Items { get; set; } = new();

    public int DistanceMeters { get; set; }

    /* S */
    public long ItemsSubtotal { get; set; }

    /* K */
    public long DiscountAmount { get; set; }

    /* Fee of the distance band before free delivery. */
    public long DeliveryFeeBase { get; set; }

    public bool FreeDeliveryApplied { get; set; }

    /* Dfee */
    public long DeliveryFee { get; set; }

    /* "Add X more for free delivery"; null when not applicable. */
    public long? FreeDeliveryRemaining { get; set; }

    /* T = S − K + Dfee */
    public long TotalAmount { get; set; }

    /* Previous unpaid debt added to this order (BR-064). */
    public long DebtCollected { get; set; }

    /* In-app credit used on this order (BR-071). */
    public long CreditApplied { get; set; }

    /* What the customer pays: T + debt − credit. */
    public long AmountDue { get; set; }

    /* false when cash is disabled after two no-shows (BR-065). */
    public bool CashAllowed { get; set; }

    public DateTime EtaMinAt { get; set; }

    public DateTime EtaMaxAt { get; set; }
}

public class QuoteLineDto
{
    public Guid MenuItemId { get; set; }

    public string Name { get; set; } = string.Empty;

    public long UnitPrice { get; set; }

    public int Quantity { get; set; }

    public long LineTotal { get; set; }
}
