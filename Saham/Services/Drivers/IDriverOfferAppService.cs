using System;
using System.Threading.Tasks;
using Saham.Services.Dtos.Drivers;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Saham.Services.Drivers;

public interface IDriverOfferAppService : IApplicationService
{
    /* null when there is no pending offer. */
    Task<DriverOfferDto?> GetCurrentAsync();

    Task<DriverTaskDto> AcceptAsync(Guid id);

    Task DeclineAsync(Guid id);

    Task<ListResultDto<DriverTaskDto>> GetTasksAsync();
}
