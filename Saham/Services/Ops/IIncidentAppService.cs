using System;
using System.Threading.Tasks;
using Saham.Services.Dtos.Ops;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Saham.Services.Ops;

public interface IIncidentAppService : IApplicationService
{
    Task<PagedResultDto<IncidentDto>> GetListAsync(GetIncidentsInput input);

    Task<IncidentDto> GetAsync(Guid id);

    Task<IncidentDto> OpenAsync(OpenIncidentDto input);

    Task<IncidentDto> AcknowledgeAsync(Guid id);

    Task<IncidentDto> AssignAsync(Guid id, AssignIncidentDto input);

    Task<IncidentDto> UpdateNoteAsync(Guid id, IncidentNoteDto input);

    Task<IncidentDto> ResolveAsync(Guid id, ResolveIncidentDto input);
}
