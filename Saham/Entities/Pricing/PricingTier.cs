using System;
using Volo.Abp.Domain.Entities;

namespace Saham.Entities.Pricing;

/* One distance band of a pricing plan (SRS table pricing_tiers): (FromM, ToM] —
 * the upper bound is inclusive (ASM-38: exactly 3,000 m is the first band). */
public class PricingTier : Entity<Guid>
{
    public Guid PricingPlanId { get; private set; }

    public int FromM { get; private set; }

    public int ToM { get; private set; }

    /* Delivery fee charged to the customer (Dbase). */
    public long CustomerFee { get; private set; }

    /* Added to the driver's base pay for this band (+1,000 above 6 km, DEC-08). */
    public long DriverExtra { get; private set; }

    /* Near and middle bands can be free above the threshold; the far band always pays (DEC-03). */
    public bool FreeDeliveryEligible { get; private set; }

    protected PricingTier()
    {
    }

    internal PricingTier(Guid id, Guid pricingPlanId, int fromM, int toM, long customerFee, long driverExtra, bool freeDeliveryEligible)
        : base(id)
    {
        PricingPlanId = pricingPlanId;
        FromM = fromM;
        ToM = toM;
        CustomerFee = customerFee;
        DriverExtra = driverExtra;
        FreeDeliveryEligible = freeDeliveryEligible;
    }

    public bool Contains(int distanceM) => distanceM > FromM && distanceM <= ToM;
}
