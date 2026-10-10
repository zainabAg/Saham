using System;
using System.Threading.Tasks;
using Saham.Services.Dtos.Admin;
using Saham.Services.Dtos.Restaurants;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Saham.Services.Admin;

public interface IRestaurantAdminAppService : IApplicationService
{
    Task<RestaurantDto> GetAsync(Guid id);

    Task<PagedResultDto<RestaurantDto>> GetListAsync(GetRestaurantsInput input);

    Task<RestaurantDto> CreateAsync(CreateRestaurantDto input);

    Task<RestaurantDto> UpdateAsync(Guid id, UpdateRestaurantDto input);

    Task<RestaurantDto> SetCommissionRateAsync(Guid id, SetCommissionRateDto input);

    Task<RestaurantDto> ActivateAsync(Guid id);

    Task<RestaurantDto> SuspendAsync(Guid id);

    Task<RestaurantStaffDto> CreateManagerAsync(Guid id, CreateRestaurantManagerDto input);
}
