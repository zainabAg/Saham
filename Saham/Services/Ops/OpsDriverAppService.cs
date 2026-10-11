using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Dispatch;
using Saham.Entities.Drivers;
using Saham.Entities.Orders;
using Saham.Permissions;
using Saham.Services.Dtos.Ops;
using Saham.Services.Dtos.Shared;
using Saham.Settings;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Settings;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Saham.Services.Ops;

public interface IOpsDriverAppService : IApplicationService
{
    Task<DriversMapDto> GetMapAsync();
}

/* REQ-OPS-02: the drivers map — every approved driver with a state ops can act on:
 *   offline        — switched off and holding no order;
 *   location stale — holding an order or online, but no fresh position (older than the
 *                    LocationFreshSeconds setting): call him (EX-02);
 *   on task        — carrying one or two orders;
 *   available      — online, fresh position, free.
 * Plus his orders in hand and a cash block (BR-066/067). */
[Authorize(SahamPermissions.OpsBoard.View)]
public class OpsDriverAppService : SahamAppService, IOpsDriverAppService
{
    private readonly IRepository<Driver, Guid> _driverRepository;
    private readonly IRepository<DeliveryAssignment, Guid> _assignmentRepository;
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IRepository<IdentityUser, Guid> _userRepository;

    public OpsDriverAppService(
        IRepository<Driver, Guid> driverRepository,
        IRepository<DeliveryAssignment, Guid> assignmentRepository,
        IRepository<Order, Guid> orderRepository,
        IRepository<IdentityUser, Guid> userRepository)
    {
        _driverRepository = driverRepository;
        _assignmentRepository = assignmentRepository;
        _orderRepository = orderRepository;
        _userRepository = userRepository;
    }

    public async Task<DriversMapDto> GetMapAsync()
    {
        var now = Clock.Now;
        var freshSeconds = await SettingProvider.GetAsync<int>(SahamSettings.Drivers.LocationFreshSeconds);

        var drivers = await _driverRepository.GetListAsync(d => d.ReviewStatus == DriverReviewStatus.Approved);
        var ids = drivers.Select(d => d.Id).ToList();

        var active = await _assignmentRepository.GetListAsync(a => ids.Contains(a.DriverId) &&
            (a.Status == AssignmentStatus.Assigned || a.Status == AssignmentStatus.PickedUp));
        var orderIds = active.Select(a => a.OrderId).ToList();
        var numbers = (await AsyncExecuter.ToListAsync((await _orderRepository.GetQueryableAsync())
                .Where(o => orderIds.Contains(o.Id)).Select(o => new { o.Id, o.Number })))
            .ToDictionary(o => o.Id, o => o.Number);
        var ordersByDriver = active.ToLookup(a => a.DriverId, a => numbers.GetValueOrDefault(a.OrderId));
        var users = (await _userRepository.GetListAsync(u => ids.Contains(u.Id))).ToDictionary(u => u.Id);

        var items = drivers.Select(d =>
            {
                var user = users.GetValueOrDefault(d.Id);
                var fresh = d.HasFreshLocation(now, freshSeconds);
                var state = d.Availability == DriverAvailability.Offline && d.ActiveOrderCount == 0
                    ? DriverMapState.Offline
                    : !fresh
                        ? DriverMapState.LocationStale
                        : d.ActiveOrderCount > 0 ? DriverMapState.OnTask : DriverMapState.Available;

                return new DriverMapItemDto
                {
                    Id = d.Id,
                    Name = user?.Name,
                    PhoneNumber = user?.PhoneNumber ?? string.Empty,
                    State = state,
                    Availability = d.Availability,
                    ActiveOrderCount = d.ActiveOrderCount,
                    ActiveOrderNumbers = ordersByDriver[d.Id].OrderBy(n => n).ToList(),
                    CashBlocked = d.CashBlocked,
                    Location = d.LastLocation == null ? null : new LocationDto { Latitude = d.LastLocation.Y, Longitude = d.LastLocation.X },
                    LastLocationAt = d.LastLocationAt,
                    LocationAgeSeconds = d.LastLocationAt.HasValue ? (int)(now - d.LastLocationAt.Value).TotalSeconds : null
                };
            })
            .OrderBy(x => x.State == DriverMapState.LocationStale ? 0 : x.State == DriverMapState.OnTask ? 1 : x.State == DriverMapState.Available ? 2 : 3)
            .ThenBy(x => x.Name)
            .ToList();

        return new DriversMapDto
        {
            Drivers = items,
            Available = items.Count(x => x.State == DriverMapState.Available),
            OnTask = items.Count(x => x.State == DriverMapState.OnTask),
            LocationStale = items.Count(x => x.State == DriverMapState.LocationStale),
            Offline = items.Count(x => x.State == DriverMapState.Offline)
        };
    }
}
