namespace Saham.Entities.Coupons;

public enum CouponDiscountType
{
    Fixed = 0,
    Percent = 1
}

/* BR-012: every coupon names who pays for its discount. */
public enum CouponFundedBy
{
    Company = 0,     // K_c
    Restaurant = 1   // K_r, out of the restaurant's own share
}

/* BR-072: the owner creates coupons; a restaurant's request waits for the owner's approval. */
public enum CouponStatus
{
    PendingApproval = 0,
    Active = 1,
    Rejected = 2,
    Disabled = 3
}

/* BR-010 / BR-011: reserved when the order is created, consumed when the order is delivered,
 * "not received" or cancelled late by the customer, released on any other cancellation. */
public enum CouponRedemptionStatus
{
    Reserved = 0,
    Consumed = 1,
    Released = 2
}
