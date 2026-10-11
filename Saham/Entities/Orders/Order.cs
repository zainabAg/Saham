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

    /* When the current search for a driver began (NO_DRIVER after N minutes, BR-032). */
    public DateTime? DispatchStartedAt { get; private set; }

    // Delivery proof (BR-034)
    public DeliveryProofMethod? ProofMethod { get; private set; }

    /* Hash of the 4-digit delivery code: what the driver types is checked against it. */
    public string? DeliveryOtpHash { get; private set; }

    /* The same code, encrypted, so the customer's tracking screen can show it (REQ-CUS-13).
     * Never returned to a driver or a restaurant. */
    public string? DeliveryOtpProtected { get; private set; }

    public int OtpFailedAttempts { get; private set; }

    public bool OtpLocked { get; private set; }

    public Guid? OverrideConfirmedBy { get; private set; }

    public string? OverrideReason { get; private set; }

    /* The cashier's "handed to the driver" (BR-074), a second confirmation of the pickup. */
    public DateTime? RestaurantHandoverAt { get; private set; }

    public Guid? HandedOverBy { get; private set; }

    /* EX-14: change the driver could not give back; credited to the customer's balance. */
    public long ChangeShortfall { get; private set; }

    // Cancellation
    public CancelReason? CancelReason { get; private set; }

    public string? CancelNote { get; private set; }

    public ActorType? CancelledByType { get; private set; }

    public Guid? CancelledBy { get; private set; }

    public FailureReason? FailureReason { get; private set; }

    /* BR-069 / BR-045: who bears the cost of a cancellation (driver pay, late-cancel charge). */
    public CancelCause? CancelChargedTo { get; private set; }

    // Coupon (BR-010..012): copied from the quote; type, value and cap let BR-048 recompute K'.
    public Guid? CouponId { get; private set; }

    public string? CouponCode { get; private set; }

    public Coupons.CouponDiscountType? CouponDiscountType { get; private set; }

    public long CouponValue { get; private set; }

    public long? CouponMaxDiscount { get; private set; }

    public Coupons.CouponFundedBy? CouponFundedBy { get; private set; }

    /* BR-047 / UC-12 A3: no decision in time → ops call the customer, with a few more minutes. */
    public DateTime? ItemDecisionEscalatedAt { get; private set; }

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

        if (s.Coupon != null)
        {
            CouponId = s.Coupon.CouponId;
            CouponCode = s.Coupon.Code;
            CouponDiscountType = s.Coupon.DiscountType;
            CouponValue = s.Coupon.Value;
            CouponMaxDiscount = s.Coupon.MaxDiscount;
            CouponFundedBy = s.Coupon.FundedBy;
        }

        foreach (var line in s.Lines)
        {
            Items.Add(new OrderItem(newId(), id, line.MenuItemId,
                new Shared.I18nText(line.Name.Ar, line.Name.En, line.Name.Fa, line.Name.Ur),
                line.UnitPrice, line.Quantity));
        }
    }

    // ---------------------------------------------------------------- creation

    /* T-02 (cash, ASAP) and T-03 (cash, scheduled). T-01: a wallet order waits in PendingPayment,
     * invisible to the restaurant, until the provider confirms the capture (BR-022). */
    internal void Place(DateTime now, DateTime? releaseAt, Func<Guid> newId, Guid customerUserId)
    {
        var target = PaymentMethod == PaymentMethod.Wallet
            ? OrderStatus.PendingPayment
            : OrderType == OrderType.Scheduled ? OrderStatus.Scheduled : OrderStatus.Placed;
        Status = target;
        ReleaseAt = releaseAt;
        if (target == OrderStatus.Placed)
        {
            PlacedAt = now;
        }

        AddHistory(newId, OrderEventType.Transition, null, target, ActorType.Customer, customerUserId, null, now);
    }

    /* T-04 / T-05: the wallet payment was captured; the order goes to the restaurant (or waits for
     * its scheduled release). A scheduled order whose release time has passed goes out at once. */
    public void ConfirmPayment(DateTime now, Func<Guid> newId)
    {
        var target = OrderType == OrderType.Scheduled && ReleaseAt.HasValue && ReleaseAt.Value > now
            ? OrderStatus.Scheduled
            : OrderStatus.Placed;
        TransitionTo(target, ActorType.System, null, "PAYMENT_CAPTURED", now, newId);
        if (target == OrderStatus.Placed)
        {
            PlacedAt = now;
        }
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
    public void ReportItemsUnavailable(IReadOnlyCollection<Guid> orderItemIds, Guid by, DateTime decisionDeadline, DateTime now, Func<Guid> newId,
        IReadOnlyCollection<ProposedSubstitute>? substitutes = null)
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

        // BR-047 (b) / ASM-30: an optional substitute per line, from the same menu, never dearer.
        foreach (var substitute in substitutes ?? Array.Empty<ProposedSubstitute>())
        {
            var original = lines.FirstOrDefault(l => l.Id == substitute.OrderItemId)
                           ?? throw new BusinessException(SahamErrorCodes.OrderItemNotFound);
            if (substitute.UnitPrice > original.UnitPrice || substitute.MenuItemId == original.MenuItemId)
            {
                throw new BusinessException(SahamErrorCodes.InvalidSubstitute).WithData("maxPrice", original.UnitPrice);
            }

            Items.Add(OrderItem.ProposeSubstitute(newId(), original, substitute.MenuItemId, substitute.Name, substitute.UnitPrice));
        }

        ItemDecisionEscalatedAt = null;
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

        var proposed = Items.Where(i => i.Status == OrderItemStatus.ProposedSubstitute).ToList();
        if (decision == ItemDecision.AcceptSubstitute && proposed.Count == 0)
        {
            throw new BusinessException(SahamErrorCodes.NoSubstituteProposed);
        }

        AddHistory(newId, OrderEventType.ItemDecision, Status, Status, actorType, by, decision.ToString(), now);

        var unavailable = Items.Where(i => i.IsReportedUnavailable).ToList();
        var remaining = Items.Where(i => i.Status == OrderItemStatus.Active && !i.IsReportedUnavailable).ToList();
        var added = decision == ItemDecision.AcceptSubstitute ? proposed : new List<OrderItem>();

        // Substitutes not taken are dropped.
        foreach (var line in proposed.Except(added))
        {
            line.Remove("SUBSTITUTE_DECLINED", now);
        }

        if (decision == ItemDecision.CancelOrder || remaining.Count + added.Count == 0)
        {
            // The restaurant is the cause (UC-12 A1/A2).
            ClearHold(actorType, by, now, newId);
            Cancel(Orders.CancelReason.ItemUnavailable, null, actorType, by, now, newId);
            return;
        }

        ChangeItems(unavailable, added, actorType, by, now, newId);
        ClearHold(actorType, by, now, newId);
    }

    /* UC-12 A3: nobody answered in time — ops call the customer, who gets a few more minutes
     * before the order continues without the items (E1). */
    public void EscalateItemDecision(DateTime newDeadline)
    {
        if (HoldReason != OrderHoldReason.ItemUnavailable || ItemDecisionEscalatedAt.HasValue)
        {
            throw new BusinessException(SahamErrorCodes.NoItemDecisionPending);
        }

        ItemDecisionEscalatedAt = ItemDecisionDeadlineAt;
        ItemDecisionDeadlineAt = newDeadline;
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

    // ---------------------------------------------------------------- dispatch (driven by DispatchManager)

    /* Accepted or ready, not on hold: the dispatch engine may look for a driver (UC-16). */
    public bool CanBeDispatched => (Status is OrderStatus.Accepted or OrderStatus.Ready) && HoldReason == null;

    /* BR-025: start when "ready" or within N minutes of the estimated ready time. */
    public bool ShouldStartDispatch(DateTime now, int minutesBeforeReady) =>
        CanBeDispatched &&
        (DispatchStatus is DispatchStatus.NotStarted or DispatchStatus.Searching) &&
        (Status == OrderStatus.Ready || (EstimatedReadyAt.HasValue && EstimatedReadyAt.Value <= now.AddMinutes(minutesBeforeReady)));

    internal void StartDispatch(DateTime now)
    {
        if (DispatchStatus == DispatchStatus.NotStarted)
        {
            DispatchStatus = DispatchStatus.Searching;
            DispatchStartedAt = now;
        }
    }

    /* A driver accepted an offer, or ops assigned one by hand (INV-02: at most one active). */
    internal void MarkAssigned(Guid driverId, long driverPay, ActorType actorType, Guid? by, DateTime now, Func<Guid> newId)
    {
        if (Status is not (OrderStatus.Placed or OrderStatus.Accepted or OrderStatus.Ready) || HoldReason != null)
        {
            throw new BusinessException(SahamErrorCodes.OrderNotDispatchable).WithData("status", Status.ToString());
        }

        DispatchStatus = DispatchStatus.Assigned;
        DriverPayAmount = driverPay;
        AddHistory(newId, OrderEventType.Assigned, Status, Status, actorType, by, driverId.ToString(), now);
    }

    /* BR-033: the assignment was released before pickup; dispatch starts again. */
    internal void MarkAssignmentReleased(string reason, ActorType actorType, Guid? by, DateTime now, Func<Guid> newId)
    {
        if (DispatchStatus == DispatchStatus.Assigned)
        {
            DispatchStatus = DispatchStatus.NotStarted;
            DispatchStartedAt = null;
        }

        AddHistory(newId, OrderEventType.Released, Status, Status, actorType, by, reason, now);
    }

    // ---------------------------------------------------------------- pickup & delivery (driven by DeliveryManager)

    /* Time of the last recorded step, used to sanity-check device times from the offline queue (EX-01). */
    public DateTime LastStepAt =>
        new[] { CreationTime, PlacedAt, AcceptedAt, ReadyAt, PickedUpAt, DeliveredAt, CancelledAt }
            .Where(t => t.HasValue).Max(t => t!.Value);

    /* T-12 (from Accepted: "ready" was never pressed) / T-13 (from Ready), by the assigned driver
     * or the cashier. A new delivery code is set at the same moment. */
    internal void MarkPickedUp(ActorType actorType, Guid by, DateTime eventTime, DateTime? clientOccurredAt,
        string otpHash, string otpProtected, Func<Guid> newId)
    {
        EnsureNotOnHold();
        if (DispatchStatus != DispatchStatus.Assigned)
        {
            throw InvalidTransition(nameof(OrderStatus.PickedUp));
        }

        var fromAccepted = Status == OrderStatus.Accepted;
        TransitionTo(OrderStatus.PickedUp, actorType, by, null, eventTime, newId, clientOccurredAt);
        if (fromAccepted)
        {
            ReadyNotMarked = true;
            ReadyAt = eventTime;
        }

        PickedUpAt = eventTime;
        DeliveryOtpHash = otpHash;
        DeliveryOtpProtected = otpProtected;
        OtpFailedAttempts = 0;
        OtpLocked = false;

        if (actorType == ActorType.Restaurant)
        {
            RestaurantHandoverAt = eventTime;
            HandedOverBy = by;
        }
    }

    /* The cashier confirms a pickup the driver already recorded (BR-074). */
    internal void RecordRestaurantHandover(Guid by, DateTime now, Func<Guid> newId)
    {
        if (Status != OrderStatus.PickedUp)
        {
            throw InvalidTransition("HandedOver");
        }

        if (RestaurantHandoverAt == null)
        {
            RestaurantHandoverAt = now;
            HandedOverBy = by;
            AddHistory(newId, OrderEventType.HandedOver, Status, Status, ActorType.Restaurant, by, null, now);
        }
    }

    /* A step that is not a status change, e.g. the driver reached the restaurant (SRS 10.4 note). */
    internal void RecordEvent(OrderEventType type, ActorType actorType, Guid? by, string? reason,
        DateTime eventTime, DateTime? clientOccurredAt, Func<Guid> newId)
    {
        AddHistory(newId, type, Status, Status, actorType, by, reason, eventTime, clientOccurredAt);
    }

    /* A wrong code; after N wrong codes the code is locked and ops take over (BR-034). */
    internal bool RegisterOtpFailure(int maxAttempts)
    {
        OtpFailedAttempts++;
        if (OtpFailedAttempts >= maxAttempts)
        {
            OtpLocked = true;
        }

        return OtpLocked;
    }

    /* T-16: only with a proof (BR-034). Ops override always records who and why. */
    internal void MarkDelivered(DeliveryProofMethod proof, ActorType actorType, Guid? by, string? overrideReason,
        DateTime eventTime, DateTime? clientOccurredAt, Func<Guid> newId)
    {
        if (proof == DeliveryProofMethod.OpsOverride && overrideReason.IsNullOrWhiteSpace())
        {
            throw new BusinessException(SahamErrorCodes.ReasonRequired);
        }

        TransitionTo(OrderStatus.Delivered, actorType, by, proof == DeliveryProofMethod.OpsOverride ? overrideReason : proof.ToString(),
            eventTime, newId, clientOccurredAt);
        DeliveredAt = eventTime;
        ProofMethod = proof;
        if (proof == DeliveryProofMethod.OpsOverride)
        {
            OverrideConfirmedBy = by;
            OverrideReason = overrideReason;
        }
    }

    /* EX-14: recorded by the driver at delivery; never more than the change owed. */
    internal void RecordChangeShortfall(long amount)
    {
        var changeDue = CashTendered.HasValue ? CashTendered.Value - AmountDue : 0;
        if (PaymentMethod != PaymentMethod.Cash || amount < 0 || amount > changeDue)
        {
            throw new BusinessException(SahamErrorCodes.ChangeShortfallTooLarge).WithData("max", changeDue);
        }

        ChangeShortfall = amount;
    }

    /* T-17: ops decide the order could not be delivered (BR-063), with a mandatory reason code. */
    internal void MarkDeliveryFailed(FailureReason reason, string? note, Guid by, DateTime now, Func<Guid> newId)
    {
        TransitionTo(OrderStatus.DeliveryFailed, ActorType.Staff, by, note.IsNullOrWhiteSpace() ? reason.ToString() : $"{reason}: {note}", now, newId);
        FailureReason = reason;
        DispatchStatus = DispatchStatus.Stopped;
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

    /* Ops may cancel any order before pickup, with a reason (order.cancel.ops ✱), and say who caused it:
     *   Company    — ops decision, no driver, closed zone: the company bears the cost;
     *   Restaurant — the restaurant cannot deliver after accepting (BR-046);
     *   Customer   — the customer asked support after acceptance (BR-045): they pay the food S − K.
     * Before acceptance the customer cancels for free from the app, so "Customer" is refused there. */
    public void CancelByOps(string note, Guid by, DateTime now, Func<Guid> newId, CancelCause cause = CancelCause.Company)
    {
        if (note.IsNullOrWhiteSpace())
        {
            throw new BusinessException(SahamErrorCodes.CancelNoteRequired);
        }

        if (cause == CancelCause.Customer && Status is not (OrderStatus.Accepted or OrderStatus.Ready))
        {
            throw new BusinessException(SahamErrorCodes.LateCancelOnlyAfterAcceptance).WithData("status", Status.ToString());
        }

        var reason = cause switch
        {
            CancelCause.Customer => Orders.CancelReason.CustomerLateCancel,
            CancelCause.Restaurant => Orders.CancelReason.RestaurantCancelled,
            _ => Orders.CancelReason.OpsCancelled
        };
        Cancel(reason, note, ActorType.Staff, by, now, newId);
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
        CancelChargedTo = reason switch
        {
            Orders.CancelReason.RestaurantRejected or Orders.CancelReason.RestaurantCancelled
                or Orders.CancelReason.RestaurantTimeout or Orders.CancelReason.ItemUnavailable => CancelCause.Restaurant,
            Orders.CancelReason.CustomerLateCancel => CancelCause.Customer,
            _ => CancelCause.Company
        };
        HoldReason = null;
        ItemDecisionDeadlineAt = null;
        DispatchStatus = DispatchStatus.Stopped; // INV-04: no offer or assignment remains
    }

    /* BR-048: unavailable lines removed, accepted substitutes added (at their price ≤ the original);
     * S' from the active items; the coupon discount recomputed with its own rules but never above
     * the old one (K' ≤ K); delivery fee never grows (a granted free delivery stays); minimum order
     * is not re-applied; C' and R' recomputed. A wallet order is refunded the difference
     * (PaymentManager), a cash order simply collects the new amount. */
    private void ChangeItems(List<OrderItem> removed, List<OrderItem> added, ActorType actorType, Guid? by, DateTime now, Func<Guid> newId)
    {
        var before = AmountsJson();
        var oldDue = AmountDue;

        foreach (var line in removed)
        {
            line.Remove("ITEM_UNAVAILABLE", now);
        }

        foreach (var line in added)
        {
            line.ActivateSubstitute();
        }

        ItemsSubtotal = Items.Where(i => i.Status == OrderItemStatus.Active).Sum(i => i.LineTotal);
        var couponLimit = CouponDiscountType.HasValue
            ? Coupons.Coupon.ComputeDiscount(CouponDiscountType.Value, CouponValue, CouponMaxDiscount, ItemsSubtotal)
            : ItemsSubtotal;
        RestaurantDiscount = Math.Min(RestaurantDiscount, Math.Min(couponLimit, ItemsSubtotal));
        CompanyDiscount = Math.Min(CompanyDiscount, Math.Min(couponLimit, ItemsSubtotal - RestaurantDiscount));
        DiscountAmount = CompanyDiscount + RestaurantDiscount;
        TotalAmount = ItemsSubtotal - DiscountAmount + DeliveryFeeCharged;
        CommissionAmount = PricingCalculator.RoundHalfUp((ItemsSubtotal - RestaurantDiscount) * CommissionRateBp, 10_000);
        RestaurantNetAmount = ItemsSubtotal - RestaurantDiscount - CommissionAmount;
        CreditApplied = Math.Min(CreditApplied, TotalAmount + DebtCollected);
        AmountDue = TotalAmount + DebtCollected - CreditApplied;

        Modifications.Add(new OrderModification(newId(), Id,
            added.Count > 0 ? OrderModificationType.ItemsSubstituted : OrderModificationType.ItemsRemoved,
            before, AmountsJson(), AmountDue - oldDue, actorType, by, now));
        AddHistory(newId, OrderEventType.Modified, Status, Status, actorType, by,
            string.Join(",", removed.Select(l => l.NameSnapshot.Ar)) +
            (added.Count > 0 ? " → " + string.Join(",", added.Select(l => l.NameSnapshot.Ar)) : string.Empty), now);
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

    private void TransitionTo(OrderStatus to, ActorType actorType, Guid? by, string? reason, DateTime now, Func<Guid> newId,
        DateTime? clientOccurredAt = null)
    {
        OrderStateMachine.EnsureTransition(Status, to);
        var from = Status;
        Status = to;
        AddHistory(newId, OrderEventType.Transition, from, to, actorType, by, reason, now, clientOccurredAt);
    }

    /* Published by ABP when the change is saved; used for realtime updates (OrderRealtimeNotifier). */
    private void RaiseChanged(OrderEventType eventType)
    {
        AddLocalEvent(new OrderChangedEto(Id, RestaurantId, CustomerId, Status, HoldReason, eventType));
    }

    private void AddHistory(Func<Guid> newId, OrderEventType type, OrderStatus? from, OrderStatus? to,
        ActorType actorType, Guid? by, string? reason, DateTime now, DateTime? clientOccurredAt = null)
    {
        History.Add(new OrderStatusHistory(newId(), Id, type, from, to, actorType, by, reason, now, clientOccurredAt));
        RaiseChanged(type);
    }

    private string AmountsJson() => JsonSerializer.Serialize(new
    {
        ItemsSubtotal, DiscountAmount, DeliveryFeeCharged, TotalAmount, CommissionAmount,
        RestaurantNetAmount, DebtCollected, CreditApplied, AmountDue,
        Items = Items.Select(i => new { i.MenuItemId, i.Quantity, i.LineTotal, Status = i.Status.ToString() })
    });
}
