using System;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Saham.Entities.Pricing;

/* Keeps exactly one active pricing plan per city (also enforced by a partial unique index). */
public class PricingPlanManager : DomainService
{
    private readonly IRepository<PricingPlan, Guid> _planRepository;

    public PricingPlanManager(IRepository<PricingPlan, Guid> planRepository)
    {
        _planRepository = planRepository;
    }

    public async Task<PricingPlan?> FindActiveAsync(Guid cityId)
    {
        var queryable = await _planRepository.WithDetailsAsync(p => p.Tiers);
        return await AsyncExecuter.FirstOrDefaultAsync(
            queryable.Where(p => p.CityId == cityId && p.Status == PricingPlanStatus.Active));
    }

    public async Task<PricingPlan> GetActiveAsync(Guid cityId)
    {
        return await FindActiveAsync(cityId)
               ?? throw new BusinessException(SahamErrorCodes.NoActivePricingPlan);
    }

    public async Task ActivateAsync(PricingPlan plan)
    {
        // Validate the draft before touching the current plan.
        plan.EnsureCanBeActivated();

        var now = Clock.Now;
        var current = await FindActiveAsync(plan.CityId);
        if (current != null && current.Id != plan.Id)
        {
            current.Retire(now);
            // Saved on its own first, so the "one active plan per city" index never sees two active rows.
            await _planRepository.UpdateAsync(current, autoSave: true);
        }

        plan.Activate(now);
        await _planRepository.UpdateAsync(plan, autoSave: true);
    }
}
