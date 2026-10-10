using System;
using System.Threading.Tasks;
using Saham.Services.Dtos.Customers;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Saham.Services.Customers;

public interface IRestaurantBrowseAppService : IApplicationService
{
    Task<ListResultDto<RestaurantSummaryDto>> GetListAsync(BrowseRestaurantsInput input);

    Task<RestaurantMenuDto> GetMenuAsync(Guid restaurantId);
}
