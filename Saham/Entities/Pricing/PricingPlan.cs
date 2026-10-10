using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Pricing;

/* Delivery fees and driver pay for a city (SRS table pricing_plans, DEC-07/DEC-08).
 * Only a Draft can be edited. Activating a draft retires the current plan; an active plan
 * is never changed, so orders keep the plan they were priced with (BR-006, BR-014). */
public class PricingPlan : AuditedAggregateRoot<Guid>
{
    public const int NameMaxLength = 128;

    public Guid CityId { get; private set; }

    public string Name { get; private set; }

    public PricingPlanStatus Status { get; private set; }

    public long DriverBasePay { get; private set; }

    public long BatchSecondOrderPay { get; private set; }

    public int CancelAfterArrivalPayPct { get; private set; }

    public long FreeDeliveryThreshold { get; private set; }

    public FreeDeliveryOperator FreeDeliveryOperator { get; private set; }

    public FreeDeliveryBasis FreeDeliveryBasis { get; private set; }

    public DistanceMethod DistanceMethod { get; private set; }

    public int MaxDeliveryDistanceM { get; private set; }

    public DateTime? EffectiveFrom { get; private set; }

    public DateTime? EffectiveTo { get; private set; }

    public ICollection<PricingTier> Tiers { get; private set; }

    protected PricingPlan()
    {
        Name = null!;
        Tiers = new List<PricingTier>();
    }

    public PricingPlan(Guid id, Guid cityId, string name)
        : base(id)
    {
        CityId = cityId;
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), NameMaxLength);
        Status = PricingPlanStatus.Draft;
        Tiers = new List<PricingTier>();
    }

    public void Update(
        string name,
        long driverBasePay,
        long batchSecondOrderPay,
        int cancelAfterArrivalPayPct,
        long freeDeliveryThreshold,
        FreeDeliveryOperator freeDeliveryOperator,
        FreeDeliveryBasis freeDeliveryBasis,
        DistanceMethod distanceMethod,
        int maxDeliveryDistanceM)
    {
        CheckDraft();

        if (driverBasePay < 0 || batchSecondOrderPay < 0 || freeDeliveryThreshold < 0 ||
            cancelAfterArrivalPayPct is < 0 or > 100 || maxDeliveryDistanceM <= 0)
        {
            throw new BusinessException(SahamErrorCodes.InvalidPricingPlan);
        }

        Name = Check.NotNullOrWhiteSpace(name, nameof(name), NameMaxLength);
        DriverBasePay = driverBasePay;
        BatchSecondOrderPay = batchSecondOrderPay;
        CancelAfterArrivalPayPct = cancelAfterArrivalPayPct;
        FreeDeliveryThreshold = freeDeliveryThreshold;
        FreeDeliveryOperator = freeDeliveryOperator;
        FreeDeliveryBasis = freeDeliveryBasis;
        DistanceMethod = distanceMethod;
        MaxDeliveryDistanceM = maxDeliveryDistanceM;
    }

    public void SetTiers(
        IEnumerable<(int FromM, int ToM, long CustomerFee, long DriverExtra, bool FreeDeliveryEligible)> tiers,
        Func<Guid> newId)
    {
        CheckDraft();

        Tiers.Clear();
        foreach (var t in tiers.OrderBy(t => t.FromM))
        {
            if (t.ToM <= t.FromM || t.FromM < 0 || t.CustomerFee < 0 || t.DriverExtra < 0)
            {
                throw new BusinessException(SahamErrorCodes.InvalidPricingTiers);
            }

            Tiers.Add(new PricingTier(newId(), Id, t.FromM, t.ToM, t.CustomerFee, t.DriverExtra, t.FreeDeliveryEligible));
        }
    }

    /* Throws if this draft cannot become the active plan. */
    public void EnsureCanBeActivated()
    {
        CheckDraft();
        CheckTiersCoverDistance();
    }

    /* Called by PricingPlanManager, which retires the previously active plan of the city. */
    internal void Activate(DateTime now)
    {
        EnsureCanBeActivated();

        Status = PricingPlanStatus.Active;
        EffectiveFrom = now;
    }

    internal void Retire(DateTime now)
    {
        Status = PricingPlanStatus.Retired;
        EffectiveTo = now;
    }

    public PricingTier? FindTier(int distanceM) => Tiers.FirstOrDefault(t => t.Contains(distanceM));

    /* Bands must run from 0 up to the maximum distance without gaps or overlaps (SRS 13.4). */
    private void CheckTiersCoverDistance()
    {
        var ordered = Tiers.OrderBy(t => t.FromM).ToList();
        if (ordered.Count == 0 || ordered[0].FromM != 0 || ordered[^1].ToM != MaxDeliveryDistanceM)
        {
            throw new BusinessException(SahamErrorCodes.InvalidPricingTiers);
        }

        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].FromM != ordered[i - 1].ToM)
            {
                throw new BusinessException(SahamErrorCodes.InvalidPricingTiers);
            }
        }
    }

    private void CheckDraft()
    {
        if (Status != PricingPlanStatus.Draft)
        {
            throw new BusinessException(SahamErrorCodes.PricingPlanNotEditable);
        }
    }
}
