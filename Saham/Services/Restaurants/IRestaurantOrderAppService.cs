using System;
using System.Threading.Tasks;
using Saham.Services.Dtos.Orders;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Saham.Services.Restaurants;

public interface IRestaurantOrderAppService : IApplicationService
{
    Task<PagedResultDto<RestaurantOrderDto>> GetListAsync(GetRestaurantOrdersInput input);

    Task<RestaurantOrderDto> GetAsync(Guid id);

    Task<RestaurantOrderDto> AcceptAsync(Guid id, AcceptOrderDto input);

    Task<RestaurantOrderDto> RejectAsync(Guid id, RejectOrderDto input);

    Task<RestaurantOrderDto> MarkReadyAsync(Guid id);

    Task<ReportItemsUnavailableResultDto> ReportItemsUnavailableAsync(Guid id, ReportItemsUnavailableDto input);

    Task<RestaurantOrderDto> CancelAsync(Guid id, CancelOrderDto input);
}
