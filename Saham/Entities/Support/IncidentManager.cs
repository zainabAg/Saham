using System;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Saham.Entities.Support;

/* Opens incidents once per problem and closes them when the problem goes away by itself. */
public class IncidentManager : DomainService
{
    private readonly IRepository<Incident, Guid> _incidentRepository;

    public IncidentManager(IRepository<Incident, Guid> incidentRepository)
    {
        _incidentRepository = incidentRepository;
    }

    public static string Key(IncidentType type, Guid subjectId) => $"{type}:{subjectId:N}";

    public async Task<Incident?> OpenAsync(
        IncidentType type, IncidentSeverity severity, Guid subjectId,
        Guid? orderId = null, Guid? driverId = null, Guid? restaurantId = null)
    {
        var key = Key(type, subjectId);
        if (await _incidentRepository.AnyAsync(i => i.DedupeKey == key && i.Status != IncidentStatus.Resolved))
        {
            return null;
        }

        return await _incidentRepository.InsertAsync(
            new Incident(GuidGenerator.Create(), type, severity, key, orderId, driverId, restaurantId, Clock.Now));
    }

    /* e.g. NO_DRIVER closes itself when a driver is finally assigned. */
    public async Task AutoResolveAsync(IncidentType type, Guid subjectId, string resolution)
    {
        var key = Key(type, subjectId);
        var open = await _incidentRepository.GetListAsync(i => i.DedupeKey == key && i.Status != IncidentStatus.Resolved);
        foreach (var incident in open)
        {
            incident.Resolve(resolution, Clock.Now);
            await _incidentRepository.UpdateAsync(incident);
        }
    }
}
