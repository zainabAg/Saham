using System;
using System.ComponentModel.DataAnnotations;
using Saham.Entities.Coupons;
using Volo.Abp.Application.Dtos;

namespace Saham.Services.Dtos.Coupons;

public class CouponDto : FullAuditedEntityDto<Guid>
{
    public string Code { get; set; } = string.Empty;

    public CouponDiscountType DiscountType { get; set; }

    public long Value { get; set; }

    public long? MaxDiscount { get; set; }

    public long MinSubtotal { get; set; }

    public DateTime StartsAt { get; set; }

    public DateTime EndsAt { get; set; }

    public int? UsageLimitTotal { get; set; }

    public int? UsageLimitPerCustomer { get; set; }

    public int UsedCount { get; set; }

    public CouponFundedBy FundedBy { get; set; }

    public bool StackableWithFreeDelivery { get; set; }

    public Guid? RestaurantId { get; set; }

    public string? RestaurantName { get; set; }

    public CouponStatus Status { get; set; }

    public Guid? RequestedBy { get; set; }

    public Guid? ApprovedBy { get; set; }

    public string? RejectionReason { get; set; }
}

/* Terms shared by the owner's coupon and the restaurant's request. */
public class CouponTermsDto
{
    [Required]
    [StringLength(Coupon.CodeMaxLength, MinimumLength = 3)]
    [RegularExpression("^[A-Za-z0-9_-]+$")]
    public string Code { get; set; } = string.Empty;

    public CouponDiscountType DiscountType { get; set; }

    /* IQD for a fixed coupon, 1..100 for a percent coupon. */
    [Range(1, long.MaxValue)]
    public long Value { get; set; }

    public long? MaxDiscount { get; set; }

    [Range(0, long.MaxValue)]
    public long MinSubtotal { get; set; }

    public DateTime StartsAt { get; set; }

    public DateTime EndsAt { get; set; }

    public int? UsageLimitTotal { get; set; }

    public int? UsageLimitPerCustomer { get; set; }
}

public class CreateCouponDto : CouponTermsDto
{
    public CouponFundedBy FundedBy { get; set; } = CouponFundedBy.Company;

    /* REC-02 default when empty: no for company coupons, yes for restaurant coupons. */
    public bool? StackableWithFreeDelivery { get; set; }

    /* Required for a restaurant-funded coupon; limits a company coupon to one restaurant. */
    public Guid? RestaurantId { get; set; }
}

public class UpdateCouponDto
{
    public CouponDiscountType DiscountType { get; set; }

    [Range(1, long.MaxValue)]
    public long Value { get; set; }

    public long? MaxDiscount { get; set; }

    [Range(0, long.MaxValue)]
    public long MinSubtotal { get; set; }

    public DateTime StartsAt { get; set; }

    public DateTime EndsAt { get; set; }

    public int? UsageLimitTotal { get; set; }

    public int? UsageLimitPerCustomer { get; set; }

    public bool StackableWithFreeDelivery { get; set; }
}

public class GetCouponsInput : PagedResultRequestDto
{
    public CouponStatus? Status { get; set; }

    public Guid? RestaurantId { get; set; }

    public string? Code { get; set; }
}

public class RejectCouponDto
{
    [Required]
    [StringLength(Coupon.ReasonMaxLength)]
    public string Reason { get; set; } = string.Empty;
}
