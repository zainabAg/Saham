using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Geography;
using Saham.Entities.Orders;
using Saham.Entities.Restaurants;
using Saham.Permissions;
using Saham.Services.Dtos.Ops;
using Saham.Settings;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Settings;

namespace Saham.Services.Ops;

public interface IOpsRestaurantAppService : IApplicationService
{
    Task<ListResultDto<RestaurantStatusDto>> GetStatusAsync();
}

/* DER-OPS-01: restaurants at a glance — open / temporarily closed / outside hours / tablet offline,
 * and the orders still waiting for their answer (oldest first is what matters, BR-023).
 * "Tablet offline" only counts while the restaurant should be taking orders. */
[Authorize(SahamPermissions.OpsBoard.View)]
public class OpsRestaurantAppService : SahamAppService, IOpsRestaurantAppService
{
    private readonly IRepository<Restaurant, Guid> _restaurantRepository;
    private readonly IRepository<City, Guid> _cityRepository;
    private readonly IRepository<Order, Guid> _orderRepository;

    public OpsRestaurantAppService(
        IRepository<Restaurant, Guid> restaurantRepository,
        IRepository<City, Guid> cityRepository,
        IRepository<Order, Guid> orderRepository)
    {
        _restaurantRepository = restaurantRepository;
        _cityRepository = cityRepository;
        _orderRepository = orderRepository;
    }

    public async Task<ListResultDto<RestaurantStatusDto>> GetStatusAsync()
    {
        var now = Clock.Now;
        var offlineAfter = await SettingProvider.GetAsync<int>(SahamSettings.Restaurants.TabletOfflineSeconds);

        var restaurants = await _restaurantRepository.GetListAsync(r => r.Status != RestaurantStatus.Pending, includeDetails: true);
        var cities = (await _cityRepository.GetListAsync()).ToDictionary(c => c.Id);

        var orders = await AsyncExecuter.ToListAsync((await _orderRepository.GetQueryableAsync())
            .Where(o => o.Status == OrderStatus.Placed || o.Status == OrderStatus.Accepted || o.Status == OrderStatus.Ready)
            .Select(o => new { o.RestaurantId, o.Status, o.PlacedAt }));
        var byRestaurant = orders.ToLookup(o => o.RestaurantId);

        var items = restaurants.Select(r =>
            {
                var local = cities.TryGetValue(r.CityId, out var city) ? city.ToLocalTime(now) : now;
                var isOpen = r.IsOpenAt(local);
                var tabletOnline = r.TabletLastSeenAt.HasValue && r.TabletLastSeenAt.Value >= now.AddSeconds(-offlineAfter);
                var pending = byRestaurant[r.Id].Where(o => o.Status == OrderStatus.Placed).ToList();

                var state = r.Status != RestaurantStatus.Active ? RestaurantBoardState.Inactive
                    : r.IsPausedAt(now) ? RestaurantBoardState.TemporarilyClosed
                    : !isOpen ? RestaurantBoardState.OutsideHours
                    : !tabletOnline ? RestaurantBoardState.TabletOffline
                    : RestaurantBoardState.Open;

                return new RestaurantStatusDto
                {
                    Id = r.Id,
                    Name = r.Name.Ar,
                    State = state,
                    Status = r.Status,
                    IsPaused = r.IsPausedAt(now),
                    PausedReason = r.PausedReason,
                    PausedUntil = r.PausedUntil,
                    IsOpenNow = isOpen,
                    TabletLastSeenAt = r.TabletLastSeenAt,
                    TabletOnline = tabletOnline,
                    PendingResponseCount = pending.Count,
                    OldestPendingSince = pending.Min(o => o.PlacedAt),
                    ActiveOrderCount = byRestaurant[r.Id].Count()
                };
            })
            // Waiting orders and offline tablets first: they are the ones ops must call.
            .OrderByDescending(x => x.PendingResponseCount > 0)
            .ThenBy(x => x.OldestPendingSince ?? DateTime.MaxValue)
            .ThenBy(x => x.State == RestaurantBoardState.TabletOffline ? 0 : x.State == RestaurantBoardState.Open ? 1 : 2)
            .ThenBy(x => x.Name)
            .ToList();

        return new ListResultDto<RestaurantStatusDto>(items);
    }
}
