using System;
using Saham.Entities.Pricing;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Coupons;

/* A discount code (SRS table coupons, BR-010..BR-012, BR-072).
 * K is computed on the items only: percent → min(round(S × pct / 100), max_discount);
 * fixed → min(value, S). Always 0 ≤ K ≤ S (BR-011).
 * UsedCount is raised atomically when an order reserves the coupon (CouponManager), so the
 * last use cannot be taken twice; CHECK (used ≤ limit) in the database as well. */
public class Coupon : FullAuditedAggregateRoot<Guid>
{
    public const int CodeMaxLength = 32;
    public const int ReasonMaxLength = 512;

    public string Code { get; private set; }

    public CouponDiscountType DiscountType { get; private set; }

    /* IQD for a fixed coupon, 1..100 for a percent coupon. */
    public long Value { get; private set; }

    public long? MaxDiscount { get; private set; }

    /* S must reach this (before discount, BR-010). */
    public long MinSubtotal { get; private set; }

    public DateTime StartsAt { get; private set; }

    public DateTime EndsAt { get; private set; }

    public int? UsageLimitTotal { get; private set; }

    public int? UsageLimitPerCustomer { get; private set; }

    public int UsedCount { get; private set; }

    public CouponFundedBy FundedBy { get; private set; }

    /* REC-02 default: no for company coupons, yes for restaurant coupons. */
    public bool StackableWithFreeDelivery { get; private set; }

    /* Required for a restaurant coupon; optional scope for a company coupon. */
    public Guid? RestaurantId { get; private set; }

    public CouponStatus Status { get; private set; }

    public Guid? RequestedBy { get; private set; }

    public Guid? ApprovedBy { get; private set; }

    public string? RejectionReason { get; private set; }

    protected Coupon()
    {
        Code = null!;
    }

    internal Coupon(
        Guid id,
        string code,
        CouponDiscountType discountType,
        long value,
        long? maxDiscount,
        long minSubtotal,
        DateTime startsAt,
        DateTime endsAt,
        int? usageLimitTotal,
        int? usageLimitPerCustomer,
        CouponFundedBy fundedBy,
        bool? stackableWithFreeDelivery,
        Guid? restaurantId)
        : base(id)
    {
        Code = NormalizeCode(code);
        FundedBy = fundedBy;
        RestaurantId = restaurantId;
        if (fundedBy == CouponFundedBy.Restaurant && restaurantId == null)
        {
            throw new BusinessException(SahamErrorCodes.CouponRestaurantRequired);
        }

        SetTerms(discountType, value, maxDiscount, minSubtotal, startsAt, endsAt, usageLimitTotal, usageLimitPerCustomer,
            stackableWithFreeDelivery ?? fundedBy == CouponFundedBy.Restaurant);
    }

    public static string NormalizeCode(string code) =>
        Check.NotNullOrWhiteSpace(code, nameof(code), CodeMaxLength).Trim().ToUpperInvariant();

    /* Terms can change only while nobody has used the coupon; afterwards only the end date and limits. */
    public void SetTerms(CouponDiscountType discountType, long value, long? maxDiscount, long minSubtotal,
        DateTime startsAt, DateTime endsAt, int? usageLimitTotal, int? usageLimitPerCustomer, bool stackableWithFreeDelivery)
    {
        if (UsedCount > 0 && (discountType != DiscountType || value != Value || maxDiscount != MaxDiscount ||
                              minSubtotal != MinSubtotal || startsAt != StartsAt || stackableWithFreeDelivery != StackableWithFreeDelivery))
        {
            throw new BusinessException(SahamErrorCodes.CouponAlreadyUsed);
        }

        if (value <= 0 || (discountType == CouponDiscountType.Percent && value > 100) ||
            maxDiscount is <= 0 || minSubtotal < 0 || endsAt <= startsAt ||
            usageLimitTotal is <= 0 || usageLimitPerCustomer is <= 0 ||
            (usageLimitTotal.HasValue && usageLimitTotal.Value < UsedCount))
        {
            throw new BusinessException(SahamErrorCodes.InvalidCouponTerms);
        }

        DiscountType = discountType;
        Value = value;
        MaxDiscount = discountType == CouponDiscountType.Percent ? maxDiscount : null;
        MinSubtotal = minSubtotal;
        StartsAt = startsAt;
        EndsAt = endsAt;
        UsageLimitTotal = usageLimitTotal;
        UsageLimitPerCustomer = usageLimitPerCustomer;
        StackableWithFreeDelivery = stackableWithFreeDelivery;
    }

    /* BR-011: K on the items only, never more than S. */
    public long DiscountFor(long itemsSubtotal) => ComputeDiscount(DiscountType, Value, MaxDiscount, itemsSubtotal);

    public static long ComputeDiscount(CouponDiscountType type, long value, long? maxDiscount, long itemsSubtotal)
    {
        var discount = type == CouponDiscountType.Percent
            ? PricingCalculator.RoundHalfUp(itemsSubtotal * value, 100)
            : value;
        if (type == CouponDiscountType.Percent && maxDiscount.HasValue)
        {
            discount = Math.Min(discount, maxDiscount.Value);
        }

        return Math.Max(0, Math.Min(discount, itemsSubtotal));
    }

    public bool IsUsableAt(DateTime now) => Status == CouponStatus.Active && now >= StartsAt && now <= EndsAt;

    public bool IsUsedUp => UsageLimitTotal.HasValue && UsedCount >= UsageLimitTotal.Value;

    // ------------------------------------------------------------------ owner / restaurant workflow

    internal void ActivateByOwner(Guid ownerId)
    {
        Status = CouponStatus.Active;
        ApprovedBy = ownerId;
        RejectionReason = null;
    }

    internal void MarkRequested(Guid requestedBy)
    {
        Status = CouponStatus.PendingApproval;
        RequestedBy = requestedBy;
    }

    public void Approve(Guid ownerId)
    {
        EnsureStatus(CouponStatus.PendingApproval);
        ActivateByOwner(ownerId);
    }

    public void Reject(Guid ownerId, string reason)
    {
        EnsureStatus(CouponStatus.PendingApproval);
        Status = CouponStatus.Rejected;
        ApprovedBy = null;
        RejectionReason = Check.NotNullOrWhiteSpace(reason, nameof(reason), ReasonMaxLength);
    }

    /* A disabled coupon takes no new orders; reservations already made stay valid. */
    public void Disable() => Status = CouponStatus.Disabled;

    public void Enable(Guid ownerId)
    {
        EnsureStatus(CouponStatus.Disabled);
        ActivateByOwner(ownerId);
    }

    private void EnsureStatus(CouponStatus expected)
    {
        if (Status != expected)
        {
            throw new BusinessException(SahamErrorCodes.InvalidCouponStatus).WithData("status", Status.ToString());
        }
    }
}
