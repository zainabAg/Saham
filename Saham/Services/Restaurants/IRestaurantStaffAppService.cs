using System;
using System.Threading.Tasks;
using Saham.Services.Dtos.Restaurants;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Saham.Services.Restaurants;

public interface IRestaurantStaffAppService : IApplicationService
{
    Task<ListResultDto<RestaurantStaffDto>> GetListAsync();

    Task<RestaurantStaffDto> CreateAsync(CreateRestaurantStaffDto input);

    Task SetActiveAsync(Guid id, SetRestaurantStaffActiveDto input);
}
