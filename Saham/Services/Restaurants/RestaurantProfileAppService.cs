using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Geography;
using Saham.Entities.Restaurants;
using Saham.Identity;
using Saham.Permissions;
using Saham.Services.Dtos.Restaurants;
using Saham.Settings;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Settings;

namespace Saham.Services.Restaurants;

/* The restaurant runs its own hours, minimum order and temporary pause (S-07):
 * - hours / minimum order / default prep time: manager (and owner) — restaurant.hours_minorder;
 * - pause / resume: manager, kitchen, ops, owner — restaurant.pause; ops and owner must give a reason (✱).
 * Restaurant staff act on their own restaurant; company staff pass restaurantId. */
[Authorize]
public class RestaurantProfileAppService : SahamAppService, IRestaurantProfileAppService
{
    private readonly IRepository<Restaurant, Guid> _restaurantRepository;
    private readonly IRepository<City, Guid> _cityRepository;
    private readonly ICurrentScope _currentScope;
    private readonly TabletPresenceTracker _tabletPresence;

    public RestaurantProfileAppService(
        IRepository<Restaurant, Guid> restaurantRepository,
        IRepository<City, Guid> cityRepository,
        ICurrentScope currentScope,
        TabletPresenceTracker tabletPresence)
    {
        _restaurantRepository = restaurantRepository;
        _cityRepository = cityRepository;
        _currentScope = currentScope;
        _tabletPresence = tabletPresence;
    }

    /* The tablet calls this every minute so ops see when it goes offline (DER-OPS-01).
     * Not audited: it is a liveness signal, not a change. */
    [DisableAuditing]
    public async Task HeartbeatAsync()
    {
        if (!await _currentScope.IsRestaurantStaffAsync())
        {
            throw new Volo.Abp.Authorization.AbpAuthorizationException();
        }

        await _tabletPresence.MarkSeenAsync(await _currentScope.GetRestaurantIdAsync());
    }

    public async Task<RestaurantProfileDto> GetAsync(Guid? restaurantId = null)
    {
        // Restaurant staff see their own restaurant; others need a company permission.
        if (!await _currentScope.IsRestaurantStaffAsync())
        {
            await AuthorizationService.CheckAsync(SahamPermissions.Restaurants.Pause);
        }

        var restaurant = await GetRestaurantAsync(restaurantId);
        return await MapAsync(restaurant);
    }

    [Authorize(SahamPermissions.Restaurants.HoursMinOrder)]
    public async Task<RestaurantProfileDto> UpdateHoursAsync(UpdateOpeningHoursDto input, Guid? restaurantId = null)
    {
        var restaurant = await GetRestaurantAsync(restaurantId);
        restaurant.SetHours(input.Shifts.Select(s => (s.DayOfWeek, s.OpensAt, s.ClosesAt)), GuidGenerator.Create);

        await _restaurantRepository.UpdateAsync(restaurant);
        return await MapAsync(restaurant);
    }

    [Authorize(SahamPermissions.Restaurants.HoursMinOrder)]
    public async Task<RestaurantProfileDto> UpdateSettingsAsync(UpdateRestaurantSettingsDto input, Guid? restaurantId = null)
    {
        var restaurant = await GetRestaurantAsync(restaurantId);
        restaurant.SetMinOrderAmount(input.MinOrderAmount);
        restaurant.SetDefaultPrepMinutes(
            input.DefaultPrepMinutes,
            await SettingProvider.GetAsync<int>(SahamSettings.Orders.MinPrepMinutes),
            await SettingProvider.GetAsync<int>(SahamSettings.Orders.MaxPrepMinutes));

        await _restaurantRepository.UpdateAsync(restaurant);
        return await MapAsync(restaurant);
    }

    [Authorize(SahamPermissions.Restaurants.Pause)]
    public async Task<RestaurantProfileDto> PauseAsync(PauseRestaurantDto input, Guid? restaurantId = null)
    {
        if (input.Reason.IsNullOrWhiteSpace() && !await _currentScope.IsRestaurantStaffAsync())
        {
            throw new BusinessException(SahamErrorCodes.PauseReasonRequired);
        }

        var restaurant = await GetRestaurantAsync(restaurantId);
        restaurant.Pause(input.Reason, input.Until, Clock.Now);

        await _restaurantRepository.UpdateAsync(restaurant);
        return await MapAsync(restaurant);
    }

    [Authorize(SahamPermissions.Restaurants.Pause)]
    public async Task<RestaurantProfileDto> ResumeAsync(Guid? restaurantId = null)
    {
        var restaurant = await GetRestaurantAsync(restaurantId);
        restaurant.Resume();

        await _restaurantRepository.UpdateAsync(restaurant);
        return await MapAsync(restaurant);
    }

    private async Task<Restaurant> GetRestaurantAsync(Guid? restaurantId)
    {
        var id = await _currentScope.ResolveRestaurantIdAsync(restaurantId);
        return await _restaurantRepository.GetAsync(id);
    }

    private async Task<RestaurantProfileDto> MapAsync(Restaurant restaurant)
    {
        var city = await _cityRepository.GetAsync(restaurant.CityId);
        var now = Clock.Now;
        var localNow = city.ToLocalTime(now);

        var dto = ObjectMapper.Map<Restaurant, RestaurantProfileDto>(restaurant);
        dto.Hours = dto.Hours.OrderBy(h => h.DayOfWeek).ThenBy(h => h.OpensAt).ToList();
        dto.IsOpenNow = restaurant.IsOpenAt(localNow);
        dto.AcceptsOrdersNow = restaurant.AcceptsOrdersAt(now, localNow);
        return dto;
    }
}
