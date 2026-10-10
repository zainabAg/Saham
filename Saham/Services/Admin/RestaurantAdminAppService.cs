using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Geography;
using Saham.Entities.Restaurants;
using Saham.Entities.Shared;
using Saham.Identity;
using Saham.Permissions;
using Saham.Services.Dtos.Admin;
using Saham.Services.Dtos.Restaurants;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Saham.Services.Admin;

/* Owner onboarding of restaurants (restaurant.onboard): create, edit, activate/suspend,
 * commission rate per restaurant (DEC-05), and the restaurant's manager account.
 * Same shape as the Book CRUD sample, with domain methods instead of public setters. */
[Authorize(SahamPermissions.Restaurants.Onboard)]
public class RestaurantAdminAppService : SahamAppService, IRestaurantAdminAppService
{
    private readonly IRepository<Restaurant, Guid> _repository;
    private readonly IRepository<City, Guid> _cityRepository;
    private readonly RestaurantStaffManager _restaurantStaffManager;

    public RestaurantAdminAppService(
        IRepository<Restaurant, Guid> repository,
        IRepository<City, Guid> cityRepository,
        RestaurantStaffManager restaurantStaffManager)
    {
        _repository = repository;
        _cityRepository = cityRepository;
        _restaurantStaffManager = restaurantStaffManager;
    }

    public async Task<RestaurantDto> GetAsync(Guid id)
    {
        var restaurant = await _repository.GetAsync(id);
        return ObjectMapper.Map<Restaurant, RestaurantDto>(restaurant);
    }

    public async Task<PagedResultDto<RestaurantDto>> GetListAsync(GetRestaurantsInput input)
    {
        var queryable = (await _repository.GetQueryableAsync())
            .WhereIf(input.CityId.HasValue, x => x.CityId == input.CityId)
            .WhereIf(input.Status.HasValue, x => x.Status == input.Status)
            .WhereIf(!input.Filter.IsNullOrWhiteSpace(),
                x => x.Name.Ar.Contains(input.Filter!) || x.Phone.Contains(input.Filter!));

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var restaurants = await AsyncExecuter.ToListAsync(queryable
            .OrderBy(input.Sorting.IsNullOrWhiteSpace() ? nameof(Restaurant.CreationTime) + " desc" : input.Sorting)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<RestaurantDto>(
            totalCount,
            ObjectMapper.Map<List<Restaurant>, List<RestaurantDto>>(restaurants));
    }

    public async Task<RestaurantDto> CreateAsync(CreateRestaurantDto input)
    {
        await _cityRepository.GetAsync(input.CityId);

        var restaurant = new Restaurant(
            GuidGenerator.Create(),
            input.CityId,
            input.Name.ToValue(),
            PhoneNumberNormalizer.Normalize(input.Phone),
            GeoFactory.CreatePoint(input.Location.Latitude, input.Location.Longitude),
            input.AddressText,
            input.CommissionRateBp);
        restaurant.UpdateProfile(restaurant.Name, restaurant.Phone, restaurant.Location, restaurant.AddressText, input.LogoUrl);

        await _repository.InsertAsync(restaurant);
        return ObjectMapper.Map<Restaurant, RestaurantDto>(restaurant);
    }

    public async Task<RestaurantDto> UpdateAsync(Guid id, UpdateRestaurantDto input)
    {
        var restaurant = await _repository.GetAsync(id);
        restaurant.UpdateProfile(
            input.Name.ToValue(),
            PhoneNumberNormalizer.Normalize(input.Phone),
            GeoFactory.CreatePoint(input.Location.Latitude, input.Location.Longitude),
            input.AddressText,
            input.LogoUrl);

        await _repository.UpdateAsync(restaurant);
        return ObjectMapper.Map<Restaurant, RestaurantDto>(restaurant);
    }

    public async Task<RestaurantDto> SetCommissionRateAsync(Guid id, SetCommissionRateDto input)
    {
        var restaurant = await _repository.GetAsync(id);
        restaurant.SetCommissionRate(input.CommissionRateBp);

        await _repository.UpdateAsync(restaurant);
        return ObjectMapper.Map<Restaurant, RestaurantDto>(restaurant);
    }

    public async Task<RestaurantDto> ActivateAsync(Guid id)
    {
        var restaurant = await _repository.GetAsync(id);
        restaurant.Activate();

        await _repository.UpdateAsync(restaurant);
        return ObjectMapper.Map<Restaurant, RestaurantDto>(restaurant);
    }

    public async Task<RestaurantDto> SuspendAsync(Guid id)
    {
        var restaurant = await _repository.GetAsync(id);
        restaurant.Suspend();

        await _repository.UpdateAsync(restaurant);
        return ObjectMapper.Map<Restaurant, RestaurantDto>(restaurant);
    }

    /* Only the owner creates a restaurant manager; the manager then creates kitchen and cashier (SOD-05). */
    public async Task<RestaurantStaffDto> CreateManagerAsync(Guid id, CreateRestaurantManagerDto input)
    {
        var (staff, user) = await _restaurantStaffManager.CreateAsync(
            id,
            PhoneNumberNormalizer.Normalize(input.PhoneNumber),
            input.Name,
            RestaurantStaffRole.Manager);

        return new RestaurantStaffDto
        {
            Id = staff.Id,
            UserId = user.Id,
            PhoneNumber = user.PhoneNumber ?? string.Empty,
            Name = user.Name,
            Role = staff.Role,
            IsActive = staff.IsActive
        };
    }
}
