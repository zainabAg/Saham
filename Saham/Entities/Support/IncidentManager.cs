using System;
using System.Linq;
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

    /* Opens unless the same problem is already open. A problem that was resolved may open again
     * (e.g. a driver whose location stops a second time). */
    public async Task<Incident?> OpenAsync(
        IncidentType type, IncidentSeverity severity, Guid subjectId,
        Guid? orderId = null, Guid? driverId = null, Guid? restaurantId = null)
    {
        var key = Key(type, subjectId);
        if (await _incidentRepository.AnyAsync(i => i.DedupeKey == key && i.Status != IncidentStatus.Resolved))
        {
            return null;
        }

        return await InsertAsync(type, severity, key, orderId, driverId, restaurantId, null);
    }

    /* Opens at most once per subject, ever: a timer that keeps seeing the same late order must not
     * reopen the incident ops already closed (RESTAURANT_NO_RESPONSE, ORDER_LATE). */
    public async Task<Incident?> OpenOnceAsync(
        IncidentType type, IncidentSeverity severity, Guid subjectId,
        Guid? orderId = null, Guid? driverId = null, Guid? restaurantId = null)
    {
        var key = Key(type, subjectId);
        if (await _incidentRepository.AnyAsync(i => i.DedupeKey == key))
        {
            return null;
        }

        return await InsertAsync(type, severity, key, orderId, driverId, restaurantId, null);
    }

    /* Opened by hand from the ops board. An open incident of the same type on the same subject
     * is returned instead of a duplicate. */
    public async Task<Incident> OpenManualAsync(
        IncidentType type, IncidentSeverity severity, Guid? orderId, Guid? driverId, Guid? restaurantId, string? note)
    {
        var subjectId = orderId ?? driverId ?? restaurantId ?? GuidGenerator.Create();
        var key = Key(type, subjectId);
        var existing = await _incidentRepository.FirstOrDefaultAsync(i => i.DedupeKey == key && i.Status != IncidentStatus.Resolved);
        return existing ?? await InsertAsync(type, severity, key, orderId, driverId, restaurantId, note);
    }

    /* e.g. NO_DRIVER closes itself when a driver is finally assigned. */
    public async Task AutoResolveAsync(IncidentType type, Guid subjectId, string resolution)
    {
        var key = Key(type, subjectId);
        var open = await _incidentRepository.GetListAsync(i => i.DedupeKey == key && i.Status != IncidentStatus.Resolved);
        foreach (var incident in open)
        {
            incident.Resolve(resolution, null, Clock.Now);
            await _incidentRepository.UpdateAsync(incident);
        }
    }

    /* Closes the given order-level incidents of one order (e.g. all of them once it is delivered). */
    public async Task AutoResolveForOrderAsync(Guid orderId, IncidentType[] types, string resolution)
    {
        var open = await _incidentRepository.GetListAsync(i =>
            i.OrderId == orderId && i.Status != IncidentStatus.Resolved && types.Contains(i.IncidentType));
        foreach (var incident in open)
        {
            incident.Resolve(resolution, null, Clock.Now);
            await _incidentRepository.UpdateAsync(incident);
        }
    }

    private Task<Incident> InsertAsync(IncidentType type, IncidentSeverity severity, string key,
        Guid? orderId, Guid? driverId, Guid? restaurantId, string? note)
    {
        return _incidentRepository.InsertAsync(
            new Incident(GuidGenerator.Create(), type, severity, key, orderId, driverId, restaurantId, Clock.Now, note));
    }
}
