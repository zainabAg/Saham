using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Coupons;

/* One coupon use by one order (SRS table coupon_redemptions; UK order_id). */
public class CouponRedemption : CreationAuditedAggregateRoot<Guid>
{
    public Guid CouponId { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid CustomerId { get; private set; }

    /* The discount actually given (K, after any later item removal — BR-048). */
    public long Amount { get; private set; }

    public CouponRedemptionStatus Status { get; private set; }

    protected CouponRedemption()
    {
    }

    internal CouponRedemption(Guid id, Guid couponId, Guid orderId, Guid customerId, long amount)
        : base(id)
    {
        CouponId = couponId;
        OrderId = orderId;
        CustomerId = customerId;
        Amount = amount;
        Status = CouponRedemptionStatus.Reserved;
    }

    internal void UpdateAmount(long amount) => Amount = amount;

    internal bool Consume()
    {
        if (Status != CouponRedemptionStatus.Reserved)
        {
            return false;
        }

        Status = CouponRedemptionStatus.Consumed;
        return true;
    }

    internal bool Release()
    {
        if (Status != CouponRedemptionStatus.Reserved)
        {
            return false;
        }

        Status = CouponRedemptionStatus.Released;
        return true;
    }
}
