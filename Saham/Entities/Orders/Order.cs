using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NetTopologySuite.Geometries;
using Saham.Entities.Pricing;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Orders;

/* An order (SRS table orders). This aggregate is the only writer of Status (SRS 14.4, M05):
 * every change goes through a method here, is checked against OrderStateMachine and
 * written to the history. Concurrent changes are rejected by the ConcurrencyStamp (409).
 *
 * Amounts (IQD, frozen at creation from the quote — BR-014):
 *   TotalAmount  = ItemsSubtotal − DiscountAmount + DeliveryFeeCharged       (T)
 *   AmountDue    = TotalAmount + DebtCollected − CreditApplied
 *   RestaurantNet= ItemsSubtotal − RestaurantDiscount − CommissionAmount     (R)
 * The same equations are CHECK constraints in the database (SRS 13.8). */
public class Order : AuditedAggregateRoot<Guid>
{
    /* Human-readable number from a database sequence (orders.order_no). */
    public long Number { get; private set; }

    public Guid CityId { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid RestaurantId { get; private set; }

    public Guid QuoteId { get; private set; }

    public Guid PricingPlanId { get; private set; }

    public OrderStatus Status { get; private set; }

    public OrderHoldReason? HoldReason { get; private set; }

    public DispatchStatus DispatchStatus { get; private set; }

    public OrderType OrderType { get; private set; }

    public DateTime? ScheduledFor { get; private set; }

    public DateTime? ReleaseAt { get; private set; }

    // Delivery address, copied from the quote (DER-ADR-01)
    public Point DeliveryLocation { get; private set; }

    public string DeliveryDescription { get; private set; }

    public string AddressLabel { get; private set; }

    public string? RecipientName { get; private set; }

    public string? RecipientPhone { get; private set; }

    public int DistanceM { get; private set; }

    // Amounts
    public long ItemsSubtotal { get; private set; }

    public long DiscountAmount { get; private set; }

    public long CompanyDiscount { get; private set; }

    public long RestaurantDiscount { get; private set; }

    public long DeliveryFeeBase { get; private set; }

    public bool FreeDeliveryApplied { get; private set; }

    public long DeliveryFeeCharged { get; private set; }

    public long TotalAmount { get; private set; }

    public int CommissionRateBp { get; private set; }

    public long CommissionAmount { get; private set; }

    public long RestaurantNetAmount { get; private set; }

    public long DriverPayAmount { get; private set; }

    public long DebtCollected { get; private set; }

    public long CreditApplied { get; private set; }

    public long AmountDue { get; private set; }

    public PaymentMethod PaymentMethod { get; private set; }

    /* The banknote the customer will pay with, for change (DER-CUS-05). */
    public long? CashTendered { get; private set; }

    // Timing
    public int TravelMinutes { get; private set; }

    public int? PrepMinutes { get; private set; }

    public DateTime? PlacedAt { get; private set; }

    public DateTime? AcceptedAt { get; private set; }

    public DateTime? EstimatedReadyAt { get; private set; }

    public DateTime? ReadyAt { get; private set; }

    public DateTime? PickedUpAt { get; private set; }

    public DateTime? DeliveredAt { get; private set; }

    public DateTime? CancelledAt { get; private set; }

    public DateTime EtaMinAt { get; private set; }

    public DateTime EtaMaxAt { get; private set; }

    public bool ReadyNotMarked { get; private set; }

    public DateTime? ItemDecisionDeadlineAt { get; private set; }

    // Cancellation
    public CancelReason? CancelReason { get; private set; }

    public string? CancelNote { get; private set; }

    public ActorType? CancelledByType { get; private set; }

    public Guid? CancelledBy { get; private set; }

    public FailureReason? FailureReason { get; private set; }

    public ICollection<OrderItem> Items { get; private set; }

    public ICollection<OrderStatusHistory> History { get; private set; }

    public ICollection<OrderModification> Modifications { get; private set; }

    protected Order()
    {
        DeliveryLocation = null!;
        DeliveryDescription = null!;
        AddressLabel = null!;
        Items = new List<OrderItem>();
        History = new List<OrderStatusHistory>();
        Modifications = new List<OrderModification>();
    }

    /* Created only by OrderManager, from a valid quote. */
    internal Order(
        Guid id,
        Guid cityId,
        Quote quote,
        PaymentMethod paymentMethod,
        long? cashTendered,
        string? recipientName,
        string? recipientPhone,
        int travelMinutes,
        Func<Guid> newId)
        : this()
    {
        Id = id;
        var s = quote.Snapshot;
        var b = s.Breakdown;

        CityId = cityId;
        CustomerId = quote.CustomerId;
        RestaurantId = quote.RestaurantId;
        QuoteId = quote.Id;
        PricingPlanId = quote.PricingPlanId;
        OrderType = quote.OrderType;
        ScheduledFor = quote.ScheduledFor;
        DispatchStatus = DispatchStatus.NotStarted;

        DeliveryLocation = Shared.GeoFactory.CreatePoint(s.Address.Latitude, s.Address.Longitude);
        DeliveryDescription = s.Address.Description;
        AddressLabel = s.Address.Label;
        RecipientName = recipientName ?? s.Address.RecipientName;
        RecipientPhone = recipientPhone ?? s.Address.RecipientPhone;
        DistanceM = b.DistanceM;

        ItemsSubtotal = b.ItemsSubtotal;
        DiscountAmount = b.DiscountAmount;
        CompanyDiscount = b.CompanyDiscount;
        RestaurantDiscount = b.RestaurantDiscount;
        DeliveryFeeBase = b.DeliveryFeeBase;
        FreeDeliveryApplied = b.FreeDeliveryApplied;
        DeliveryFeeCharged = b.DeliveryFee;
        TotalAmount = b.TotalAmount;
        CommissionRateBp = b.CommissionRateBp;
        CommissionAmount = b.CommissionAmount;
        RestaurantNetAmount = b.RestaurantNetAmount;
        DriverPayAmount = b.DriverPayAmount;
        DebtCollected = s.DebtCollected;
        CreditApplied = s.CreditApplied;
        AmountDue = s.AmountDue;
        PaymentMethod = paymentMethod;
        CashTendered = cashTendered;

        TravelMinutes = travelMinutes;
        EtaMinAt = s.EtaMinAt;
        EtaMaxAt = s.EtaMaxAt;

        foreach (var line in s.Lines)
        {
            Items.Add(new OrderItem(newId(), id, line.MenuItemId,
                new Shared.I18nText(line.Name.Ar, line.Name.En, line.Name.Fa, line.Name.Ur),
                line.UnitPrice, line.Quantity));
        }
    }

    // ---------------------------------------------------------------- creation

    /* T-02 (cash, ASAP) and T-03 (cash, scheduled). Wallet orders start in PendingPayment (phase 9). */
    internal void Place(DateTime now, DateTime? releaseAt, Func<Guid> newId, Guid customerUserId)
    {
        var target = OrderType == OrderType.Scheduled ? OrderStatus.Scheduled : OrderStatus.Placed;
        Status = target;
        ReleaseAt = releaseAt;
        if (target == OrderStatus.Placed)
        {
            PlacedAt = now;
        }

        AddHistory(newId, OrderEventType.Transition, null, target, ActorType.Customer, customerUserId, null, now);
    }

    // ---------------------------------------------------------------- restaurant

    /* T-07: the scheduled order is sent to the restaurant at its release time. */
    public void Release(DateTime now, Func<Guid> newId)
    {
        TransitionTo(OrderStatus.Placed, ActorType.System, null, null, now, newId);
        PlacedAt = now;
    }

    /* T-09: accepted with a preparation time of 5..120 minutes (BR-024). */
    public void Accept(int prepMinutes, int minPrep, int maxPrep, Guid by, DateTime now, Func<Guid> newId)
    {
        if (prepMinutes < minPrep || prepMinutes > maxPrep)
        {
            throw new BusinessException(SahamErrorCodes.InvalidPrepMinutes).WithData("min", minPrep).WithData("max", maxPrep);
        }

        TransitionTo(OrderStatus.Accepted, ActorType.Restaurant, by, null, now, newId);
        PrepMinutes = prepMinutes;
        AcceptedAt = now;
        EstimatedReadyAt = now.AddMinutes(prepMinutes);

        // The estimate shown to the customer now follows the restaurant's own preparation time.
        var range = EtaMaxAt - EtaMinAt;
        EtaMinAt = EstimatedReadyAt.Value.AddMinutes(TravelMinutes);
        EtaMaxAt = EtaMinAt + range;
    }

    /* T-10: rejection with a reason from the list (REQ-RST-04). */
    public void Reject(RestaurantRejectReason reason, string? note, Guid by, DateTime now, Func<Guid> newId)
    {
        if (Status != OrderStatus.Placed)
        {
            throw InvalidTransition("Rejected");
        }

        Cancel(Orders.CancelReason.RestaurantRejected, $"{reason}{(note.IsNullOrWhiteSpace() ? "" : ": " + note)}",
            ActorType.Restaurant, by, now, newId);
    }

    /* T-11: blocked while waiting for an item decision (BR-047). */
    public void MarkReady(Guid by, DateTime now, Func<Guid> newId)
    {
        EnsureNotOnHold();
        TransitionTo(OrderStatus.Ready, ActorType.Restaurant, by, null, now, newId);
        ReadyAt = now;
    }

    /* BR-047: the kitchen reports items that ran out after acceptance. The order is held,
     * dispatch is paused, and the customer has a few minutes to decide. */
    public void ReportItemsUnavailable(IReadOnlyCollection<Guid> orderItemIds, Guid by, DateTime decisionDeadline, DateTime now, Func<Guid> newId)
    {
        if (Status is not (OrderStatus.Accepted or OrderStatus.Ready))
        {
            throw InvalidTransition("Hold");
        }

        EnsureNotOnHold();

        var lines = Items.Where(i => orderItemIds.Contains(i.Id) && i.Status == OrderItemStatus.Active).ToList();
        if (lines.Count == 0 || lines.Count != orderItemIds.Distinct().Count())
        {
            throw new BusinessException(SahamErrorCodes.OrderItemNotFound);
        }

        foreach (var line in lines)
        {
            line.ReportUnavailable();
        }

        HoldReason = OrderHoldReason.ItemUnavailable;
        ItemDecisionDeadlineAt = decisionDeadline;
        if (DispatchStatus is DispatchStatus.NotStarted or DispatchStatus.Searching)
        {
            DispatchStatus = DispatchStatus.Paused; // INV-06
        }

        AddHistory(newId, OrderEventType.Hold, Status, Status, ActorType.Restaurant, by,
            string.Join(",", lines.Select(l => l.NameSnapshot.Ar)), now);
    }

    /* The customer's (or ops', or the timer's) answer to an unavailable item (BR-047/048). */
    public void ResolveItemDecision(ItemDecision decision, ActorType actorType, Guid? by, DateTime now, Func<Guid> newId)
    {
        if (HoldReason != OrderHoldReason.ItemUnavailable)
        {
            throw new BusinessException(SahamErrorCodes.NoItemDecisionPending);
        }

        AddHistory(newId, OrderEventType.ItemDecision, Status, Status, actorType, by, decision.ToString(), now);

        var unavailable = Items.Where(i => i.IsReportedUnavailable).ToList();
        var remaining = Items.Where(i => i.Status == OrderItemStatus.Active && !i.IsReportedUnavailable).ToList();

        if (decision == ItemDecision.CancelOrder || remaining.Count == 0)
        {
            // The restaurant is the cause (UC-12 A1/A2).
            ClearHold(actorType, by, now, newId);
            Cancel(Orders.CancelReason.ItemUnavailable, null, actorType, by, now, newId);
            return;
        }

        RemoveItems(unavailable, actorType, by, now, newId);
        ClearHold(actorType, by, now, newId);
    }

    /* T-14 / T-15: the restaurant cancels an accepted order, reason mandatory (BR-046). */
    public void CancelByRestaurant(string note, Guid by, DateTime now, Func<Guid> newId)
    {
        if (note.IsNullOrWhiteSpace())
        {
            throw new BusinessException(SahamErrorCodes.CancelNoteRequired);
        }

        if (Status is not (OrderStatus.Accepted or OrderStatus.Ready))
        {
            throw InvalidTransition(nameof(OrderStatus.Cancelled));
        }

        Cancel(Orders.CancelReason.RestaurantCancelled, note, ActorType.Restaurant, by, now, newId);
    }

    // ---------------------------------------------------------------- customer / ops / system

    /* BR-045: the customer cancels for free before the restaurant accepts. */
    public void CancelByCustomer(Guid by, DateTime now, Func<Guid> newId)
    {
        if (Status is not (OrderStatus.PendingPayment or OrderStatus.Scheduled or OrderStatus.Placed))
        {
            throw new BusinessException(SahamErrorCodes.CustomerCannotCancel).WithData("status", Status.ToString());
        }

        Cancel(Orders.CancelReason.CustomerCancelled, null, ActorType.Customer, by, now, newId);
    }

    /* Ops may cancel any order before pickup, with a reason (order.cancel.ops ✱). */
    public void CancelByOps(string note, Guid by, DateTime now, Func<Guid> newId)
    {
        if (note.IsNullOrWhiteSpace())
        {
            throw new BusinessException(SahamErrorCodes.CancelNoteRequired);
        }

        Cancel(Orders.CancelReason.OpsCancelled, note, ActorType.Staff, by, now, newId);
    }

    /* BR-023 restaurant timeout, BR-021 closed at release, etc. */
    public void CancelBySystem(CancelReason reason, DateTime now, Func<Guid> newId)
    {
        Cancel(reason, null, ActorType.System, null, now, newId);
    }

    // ---------------------------------------------------------------- helpers

    public bool IsFinal => OrderStateMachine.IsFinal(Status);

    private void Cancel(CancelReason reason, string? note, ActorType actorType, Guid? by, DateTime now, Func<Guid> newId)
    {
        TransitionTo(OrderStatus.Cancelled, actorType, by, note ?? reason.ToString(), now, newId);
        CancelReason = reason;
        CancelNote = note;
        CancelledByType = actorType;
        CancelledBy = by;
        CancelledAt = now;
        HoldReason = null;
        ItemDecisionDeadlineAt = null;
        DispatchStatus = DispatchStatus.Stopped; // INV-04: no offer or assignment remains
    }

    /* BR-048: S' from remaining items; discount never grows; delivery fee never grows
     * (a granted free delivery stays); minimum order is not re-applied; C' and R' recomputed. */
    private void RemoveItems(List<OrderItem> lines, ActorType actorType, Guid? by, DateTime now, Func<Guid> newId)
    {
        var before = AmountsJson();
        var oldDue = AmountDue;

        foreach (var line in lines)
        {
            line.Remove("ITEM_UNAVAILABLE", now);
        }

        ItemsSubtotal = Items.Where(i => i.Status == OrderItemStatus.Active).Sum(i => i.LineTotal);
        RestaurantDiscount = Math.Min(RestaurantDiscount, ItemsSubtotal);
        CompanyDiscount = Math.Min(CompanyDiscount, ItemsSubtotal - RestaurantDiscount);
        DiscountAmount = CompanyDiscount + RestaurantDiscount;
        TotalAmount = ItemsSubtotal - DiscountAmount + DeliveryFeeCharged;
        CommissionAmount = PricingCalculator.RoundHalfUp((ItemsSubtotal - RestaurantDiscount) * CommissionRateBp, 10_000);
        RestaurantNetAmount = ItemsSubtotal - RestaurantDiscount - CommissionAmount;
        CreditApplied = Math.Min(CreditApplied, TotalAmount + DebtCollected);
        AmountDue = TotalAmount + DebtCollected - CreditApplied;

        Modifications.Add(new OrderModification(newId(), Id, OrderModificationType.ItemsRemoved,
            before, AmountsJson(), AmountDue - oldDue, actorType, by, now));
        AddHistory(newId, OrderEventType.Modified, Status, Status, actorType, by,
            string.Join(",", lines.Select(l => l.NameSnapshot.Ar)), now);
    }

    private void ClearHold(ActorType actorType, Guid? by, DateTime now, Func<Guid> newId)
    {
        foreach (var line in Items.Where(i => i.IsReportedUnavailable))
        {
            line.ClearUnavailableReport();
        }

        HoldReason = null;
        ItemDecisionDeadlineAt = null;
        if (DispatchStatus == DispatchStatus.Paused)
        {
            DispatchStatus = DispatchStatus.NotStarted; // dispatch resumes (phase 5)
        }

        AddHistory(newId, OrderEventType.Unhold, Status, Status, actorType, by, null, now);
    }

    private BusinessException InvalidTransition(string to) =>
        new BusinessException(SahamErrorCodes.InvalidStateTransition)
            .WithData("from", Status.ToString())
            .WithData("to", to);

    private void EnsureNotOnHold()
    {
        if (HoldReason.HasValue)
        {
            throw new BusinessException(SahamErrorCodes.OrderOnHold);
        }
    }

    private void TransitionTo(OrderStatus to, ActorType actorType, Guid? by, string? reason, DateTime now, Func<Guid> newId)
    {
        OrderStateMachine.EnsureTransition(Status, to);
        var from = Status;
        Status = to;
        AddHistory(newId, OrderEventType.Transition, from, to, actorType, by, reason, now);
    }

    /* Published by ABP when the change is saved; used for realtime updates (OrderRealtimeNotifier). */
    private void RaiseChanged(OrderEventType eventType)
    {
        AddLocalEvent(new OrderChangedEto(Id, RestaurantId, CustomerId, Status, HoldReason, eventType));
    }

    private void AddHistory(Func<Guid> newId, OrderEventType type, OrderStatus? from, OrderStatus? to,
        ActorType actorType, Guid? by, string? reason, DateTime now)
    {
        History.Add(new OrderStatusHistory(newId(), Id, type, from, to, actorType, by, reason, now));
        RaiseChanged(type);
    }

    private string AmountsJson() => JsonSerializer.Serialize(new
    {
        ItemsSubtotal, DiscountAmount, DeliveryFeeCharged, TotalAmount, CommissionAmount,
        RestaurantNetAmount, DebtCollected, CreditApplied, AmountDue,
        Items = Items.Select(i => new { i.MenuItemId, i.Quantity, i.LineTotal, Status = i.Status.ToString() })
    });
}
