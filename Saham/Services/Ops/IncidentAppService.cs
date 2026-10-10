using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Support;
using Saham.Permissions;
using Saham.Services.Dtos.Drivers;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace Saham.Services.Ops;

/* The ops incident queue (REQ-OPS-02). Highest severity and oldest first.
 * More incident types and the full board arrive in phase 8. */
[Authorize(SahamPermissions.Incidents.Handle)]
public class IncidentAppService : SahamAppService, IIncidentAppService
{
    private readonly IRepository<Incident, Guid> _incidentRepository;

    public IncidentAppService(IRepository<Incident, Guid> incidentRepository)
    {
        _incidentRepository = incidentRepository;
    }

    public async Task<PagedResultDto<IncidentDto>> GetListAsync(GetIncidentsInput input)
    {
        var queryable = (await _incidentRepository.GetQueryableAsync())
            .WhereIf(input.OpenOnly, i => i.Status != IncidentStatus.Resolved)
            .WhereIf(input.IncidentType.HasValue, i => i.IncidentType == input.IncidentType);

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var incidents = await AsyncExecuter.ToListAsync(queryable
            .OrderByDescending(i => i.Severity)
            .ThenBy(i => i.OpenedAt)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<IncidentDto>(totalCount, ObjectMapper.Map<List<Incident>, List<IncidentDto>>(incidents));
    }

    public async Task<IncidentDto> AcknowledgeAsync(Guid id)
    {
        var incident = await _incidentRepository.GetAsync(id);
        incident.Acknowledge(CurrentUser.GetId());

        await _incidentRepository.UpdateAsync(incident);
        return ObjectMapper.Map<Incident, IncidentDto>(incident);
    }

    public async Task<IncidentDto> ResolveAsync(Guid id, ResolveIncidentDto input)
    {
        var incident = await _incidentRepository.GetAsync(id);
        incident.Resolve(input.Resolution, Clock.Now);

        await _incidentRepository.UpdateAsync(incident);
        return ObjectMapper.Map<Incident, IncidentDto>(incident);
    }
}
