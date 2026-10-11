using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Dispatch;
using Saham.Entities.Drivers;
using Saham.Entities.Orders;
using Saham.Entities.Support;
using Saham.Hubs;
using Saham.Entities.Shared;
using Saham.Identity;
using Saham.Permissions;
using Saham.Services.Dtos.Drivers;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Repositories;

namespace Saham.Services.Drivers;

/* GPS from the driver app (REQ-DRV-05, ASM-12): every 15 s on a task, every 60 s when waiting,
 * and the offline queue in one batch after a gap (EX-01). A device time later than
 * "now + 2 minutes" is not trusted and replaced by the server time. Not audited: high volume. */
[Authorize(SahamPermissions.Dispatch.OfferRespond)]
[DisableAuditing]
public class DriverLocationAppService : SahamAppService, IDriverLocationAppService
{
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(2);

    private readonly IRepository<Driver, Guid> _driverRepository;
    private readonly IRepository<DriverLocation, long> _locationRepository;
    private readonly ICurrentScope _currentScope;
    private readonly IRepository<DeliveryAssignment, Guid> _assignmentRepository;
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IncidentManager _incidentManager;
    private readonly CustomerTrackingNotifier _trackingNotifier;

    public DriverLocationAppService(
        IRepository<Driver, Guid> driverRepository,
        IRepository<DriverLocation, long> locationRepository,
        ICurrentScope currentScope,
        IRepository<DeliveryAssignment, Guid> assignmentRepository,
        IRepository<Order, Guid> orderRepository,
        IncidentManager incidentManager,
        CustomerTrackingNotifier trackingNotifier)
    {
        _driverRepository = driverRepository;
        _locationRepository = locationRepository;
        _currentScope = currentScope;
        _assignmentRepository = assignmentRepository;
        _orderRepository = orderRepository;
        _incidentManager = incidentManager;
        _trackingNotifier = trackingNotifier;
    }

    public async Task<PostLocationsResultDto> PostAsync(PostLocationsDto input)
    {
        var driver = await _currentScope.GetDriverAsync();
        var now = Clock.Now;

        var points = input.Points
            .Select(p => new DriverLocation(
                driver.Id,
                p.OrderId,
                GeoFactory.CreatePoint(p.Latitude, p.Longitude),
                p.AccuracyM,
                p.RecordedAt > now.Add(MaxClockSkew) ? now : p.RecordedAt,
                now))
            .OrderBy(p => p.RecordedAt)
            .ToList();

        await _locationRepository.InsertManyAsync(points);

        var latest = points[^1];
        driver.UpdateLocation(latest.Location, latest.RecordedAt);
        await _driverRepository.UpdateAsync(driver);

        if (driver.ActiveOrderCount > 0)
        {
            // The phone is back: close a DRIVER_STALE incident and show the position to the customers.
            await _incidentManager.AutoResolveAsync(IncidentType.DriverStale, driver.Id, "LOCATION_RESUMED");

            var orderIds = (await _assignmentRepository.GetListAsync(a => a.DriverId == driver.Id &&
                    (a.Status == AssignmentStatus.Assigned || a.Status == AssignmentStatus.PickedUp)))
                .Select(a => a.OrderId).ToList();
            foreach (var order in await _orderRepository.GetListAsync(o => orderIds.Contains(o.Id)))
            {
                _trackingNotifier.Queue(order.CustomerId, order.Id, latest.Location.Y, latest.Location.X, latest.RecordedAt);
            }
        }

        return new PostLocationsResultDto { Accepted = points.Count, LastLocationAt = driver.LastLocationAt };
    }
}
