using System;
using System.Threading.Tasks;
using Saham.Services.Dtos.Finance;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Saham.Services.Finance;

public interface IBalancesAppService : IApplicationService
{
    Task<ListResultDto<DriverBalanceDto>> GetDriversAsync();

    Task<ListResultDto<RestaurantBalanceDto>> GetRestaurantsAsync();

    Task<ListResultDto<JournalEntryDto>> GetOrderEntriesAsync(Guid orderId);
}
