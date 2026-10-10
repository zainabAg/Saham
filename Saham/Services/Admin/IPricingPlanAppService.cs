using System;
using System.Threading.Tasks;
using Saham.Services.Dtos.Admin;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Saham.Services.Admin;

public interface IPricingPlanAppService : IApplicationService
{
    Task<ListResultDto<PricingPlanDto>> GetListAsync(GetPricingPlansInput input);

    Task<PricingPlanDto> GetAsync(Guid id);

    Task<PricingPlanDto> GetActiveAsync(Guid cityId);

    Task<PricingPlanDto> CreateAsync(CreateUpdatePricingPlanDto input);

    Task<PricingPlanDto> CreateFromActiveAsync(Guid cityId);

    Task<PricingPlanDto> UpdateAsync(Guid id, CreateUpdatePricingPlanDto input);

    Task<PricingPlanDto> ActivateAsync(Guid id);
}
