using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Geography;
using Saham.Entities.Pricing;
using Saham.Permissions;
using Saham.Services.Dtos.Admin;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Saham.Services.Admin;

/* The owner sets delivery fees, driver pay and the free-delivery threshold (pricing.manage ✱).
 * Changing prices = create a draft (optionally a copy of the active plan), edit it, activate it.
 * The previous plan is retired, never edited, so existing orders keep their prices. */
[Authorize(SahamPermissions.Pricing.Manage)]
public class PricingPlanAppService : SahamAppService, IPricingPlanAppService
{
    private readonly IRepository<PricingPlan, Guid> _planRepository;
    private readonly IRepository<City, Guid> _cityRepository;
    private readonly PricingPlanManager _planManager;

    public PricingPlanAppService(
        IRepository<PricingPlan, Guid> planRepository,
        IRepository<City, Guid> cityRepository,
        PricingPlanManager planManager)
    {
        _planRepository = planRepository;
        _cityRepository = cityRepository;
        _planManager = planManager;
    }

    public async Task<ListResultDto<PricingPlanDto>> GetListAsync(GetPricingPlansInput input)
    {
        var queryable = (await _planRepository.WithDetailsAsync(p => p.Tiers))
            .WhereIf(input.CityId.HasValue, p => p.CityId == input.CityId)
            .WhereIf(input.Status.HasValue, p => p.Status == input.Status)
            .OrderByDescending(p => p.CreationTime);

        var plans = await AsyncExecuter.ToListAsync(queryable);
        return new ListResultDto<PricingPlanDto>(ObjectMapper.Map<List<PricingPlan>, List<PricingPlanDto>>(plans));
    }

    public async Task<PricingPlanDto> GetAsync(Guid id)
    {
        return Map(await _planRepository.GetAsync(id));
    }

    public async Task<PricingPlanDto> GetActiveAsync(Guid cityId)
    {
        return Map(await _planManager.GetActiveAsync(cityId));
    }

    public async Task<PricingPlanDto> CreateAsync(CreateUpdatePricingPlanDto input)
    {
        await _cityRepository.GetAsync(input.CityId);

        var plan = new PricingPlan(GuidGenerator.Create(), input.CityId, input.Name);
        Apply(plan, input);

        await _planRepository.InsertAsync(plan);
        return Map(plan);
    }

    /* Starting point for a price change: a draft with the current values. */
    public async Task<PricingPlanDto> CreateFromActiveAsync(Guid cityId)
    {
        var active = await _planManager.GetActiveAsync(cityId);

        var draft = new PricingPlan(GuidGenerator.Create(), cityId, active.Name);
        draft.Update(
            active.Name,
            active.DriverBasePay,
            active.BatchSecondOrderPay,
            active.CancelAfterArrivalPayPct,
            active.FreeDeliveryThreshold,
            active.FreeDeliveryOperator,
            active.FreeDeliveryBasis,
            active.DistanceMethod,
            active.MaxDeliveryDistanceM);
        draft.SetTiers(
            active.Tiers.Select(t => (t.FromM, t.ToM, t.CustomerFee, t.DriverExtra, t.FreeDeliveryEligible)),
            GuidGenerator.Create);

        await _planRepository.InsertAsync(draft);
        return Map(draft);
    }

    public async Task<PricingPlanDto> UpdateAsync(Guid id, CreateUpdatePricingPlanDto input)
    {
        var plan = await _planRepository.GetAsync(id);
        if (plan.CityId != input.CityId)
        {
            throw new BusinessException(SahamErrorCodes.InvalidPricingPlan);
        }

        Apply(plan, input);

        await _planRepository.UpdateAsync(plan);
        return Map(plan);
    }

    public async Task<PricingPlanDto> ActivateAsync(Guid id)
    {
        var plan = await _planRepository.GetAsync(id);
        await _planManager.ActivateAsync(plan);
        return Map(plan);
    }

    private void Apply(PricingPlan plan, CreateUpdatePricingPlanDto input)
    {
        plan.Update(
            input.Name,
            input.DriverBasePay,
            input.BatchSecondOrderPay,
            input.CancelAfterArrivalPayPct,
            input.FreeDeliveryThreshold,
            input.FreeDeliveryOperator,
            input.FreeDeliveryBasis,
            input.DistanceMethod,
            input.MaxDeliveryDistanceM);
        plan.SetTiers(
            input.Tiers.Select(t => (t.FromM, t.ToM, t.CustomerFee, t.DriverExtra, t.FreeDeliveryEligible)),
            GuidGenerator.Create);
    }

    private PricingPlanDto Map(PricingPlan plan)
    {
        var dto = ObjectMapper.Map<PricingPlan, PricingPlanDto>(plan);
        dto.Tiers = dto.Tiers.OrderBy(t => t.FromM).ToList();
        return dto;
    }
}
