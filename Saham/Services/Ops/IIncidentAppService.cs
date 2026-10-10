using System;
using System.Threading.Tasks;
using Saham.Services.Dtos.Drivers;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Saham.Services.Ops;

public interface IIncidentAppService : IApplicationService
{
    Task<PagedResultDto<IncidentDto>> GetListAsync(GetIncidentsInput input);

    Task<IncidentDto> AcknowledgeAsync(Guid id);

    Task<IncidentDto> ResolveAsync(Guid id, ResolveIncidentDto input);
}
