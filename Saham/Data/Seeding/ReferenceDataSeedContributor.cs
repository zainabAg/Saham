using System;
using System.Threading.Tasks;
using System.Linq;
using Saham.Entities.Geography;
using Saham.Entities.Pricing;
using Saham.Entities.Shared;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Uow;

namespace Saham.Data.Seeding;

/* Reference rows every environment needs:
 * - the operating city (Karbala, S-18); its service-area polygon is left for the owner to draw (DER-ADR-07);
 * - the pricing plan approved by the client (DEC-07, DEC-08, SRS 12.6):
 *     (0–3 km] 2,000 · (3–6 km] 3,000 · (6–10 km] 4,000 (never free, driver +1,000),
 *     free delivery above 25,000 after discount, driver 1,500, batched second order 1,000. */
public class ReferenceDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    public static readonly Guid KarbalaCityId = Guid.Parse("6f1c3a52-9d4e-4b8a-a1d2-3c5e7f9b0a11");

    private readonly IRepository<City, Guid> _cityRepository;
    private readonly IRepository<PricingPlan, Guid> _planRepository;
    private readonly PricingPlanManager _planManager;
    private readonly IGuidGenerator _guidGenerator;

    public ReferenceDataSeedContributor(
        IRepository<City, Guid> cityRepository,
        IRepository<PricingPlan, Guid> planRepository,
        PricingPlanManager planManager,
        IGuidGenerator guidGenerator)
    {
        _cityRepository = cityRepository;
        _planRepository = planRepository;
        _planManager = planManager;
        _guidGenerator = guidGenerator;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        if (await _cityRepository.FindAsync(KarbalaCityId) == null)
        {
            await _cityRepository.InsertAsync(new City(
                KarbalaCityId,
                new I18nText("كربلاء", "Karbala", "کربلا", "کربلا"),
                GeoFactory.CreatePoint(32.6160, 44.0249)), autoSave: true);
        }

        if (!await _planRepository.AnyAsync(p => p.CityId == KarbalaCityId))
        {
            await SeedPricingPlanAsync();
        }
    }

    private async Task SeedPricingPlanAsync()
    {
        var plan = new PricingPlan(_guidGenerator.Create(), KarbalaCityId, "أسعار كربلاء المعتمدة");
        plan.Update(
            plan.Name,
            driverBasePay: 1_500,
            batchSecondOrderPay: 1_000,
            cancelAfterArrivalPayPct: 50,
            freeDeliveryThreshold: 25_000,
            FreeDeliveryOperator.GreaterThan,
            FreeDeliveryBasis.AfterDiscount,
            DistanceMethod.StraightLine,
            maxDeliveryDistanceM: 10_000);
        plan.SetTiers(new[]
        {
            (0, 3_000, 2_000L, 0L, true),
            (3_000, 6_000, 3_000L, 0L, true),
            (6_000, 10_000, 4_000L, 1_000L, false)
        }, _guidGenerator.Create);

        await _planRepository.InsertAsync(plan, autoSave: true);
        await _planManager.ActivateAsync(plan);
    }
}
