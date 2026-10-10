using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using NetTopologySuite.Geometries;
using Saham.Entities.Geography;
using Saham.Entities.Restaurants;
using Saham.Entities.Shared;
using Saham.Permissions;
using Saham.Services.Dtos.Customers;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace Saham.Services.Customers;

/* The customer's home screen and menu (REQ-CUS-01/02):
 * restaurants that accept orders now come first; closed ones are listed separately with
 * "closed — opens at …" (scheduled ordering only). Only active restaurants are shown. */
[Authorize(SahamPermissions.Restaurants.Browse)]
public class RestaurantBrowseAppService : SahamAppService, IRestaurantBrowseAppService
{
    private readonly IRepository<Restaurant, Guid> _restaurantRepository;
    private readonly IRepository<City, Guid> _cityRepository;
    private readonly IRepository<MenuCategory, Guid> _categoryRepository;
    private readonly IRepository<MenuItem, Guid> _itemRepository;

    public RestaurantBrowseAppService(
        IRepository<Restaurant, Guid> restaurantRepository,
        IRepository<City, Guid> cityRepository,
        IRepository<MenuCategory, Guid> categoryRepository,
        IRepository<MenuItem, Guid> itemRepository)
    {
        _restaurantRepository = restaurantRepository;
        _cityRepository = cityRepository;
        _categoryRepository = categoryRepository;
        _itemRepository = itemRepository;
    }

    public async Task<ListResultDto<RestaurantSummaryDto>> GetListAsync(BrowseRestaurantsInput input)
    {
        var city = await GetCityAsync(input.CityId);
        var customerPoint = input.Latitude.HasValue && input.Longitude.HasValue
            ? GeoFactory.CreatePoint(input.Latitude.Value, input.Longitude.Value)
            : null;

        var queryable = (await _restaurantRepository.WithDetailsAsync(r => r.Hours))
            .Where(r => r.CityId == city.Id && r.Status == RestaurantStatus.Active)
            .WhereIf(!input.Filter.IsNullOrWhiteSpace(),
                r => r.Name.Ar.Contains(input.Filter!) || (r.Name.En != null && r.Name.En.Contains(input.Filter!)));

        var restaurants = await AsyncExecuter.ToListAsync(queryable);

        var items = restaurants
            .Select(r => ToSummary(r, city, customerPoint))
            .OrderByDescending(r => r.AcceptsOrdersNow)
            .ThenBy(r => r.DistanceMeters ?? double.MaxValue)
            .ThenBy(r => r.Name)
            .ToList();

        return new ListResultDto<RestaurantSummaryDto>(items);
    }

    public async Task<RestaurantMenuDto> GetMenuAsync(Guid restaurantId)
    {
        var restaurant = await _restaurantRepository.GetAsync(restaurantId);
        if (restaurant.Status != RestaurantStatus.Active)
        {
            throw new EntityNotFoundException(typeof(Restaurant), restaurantId);
        }

        var city = await _cityRepository.GetAsync(restaurant.CityId);
        var categories = await _categoryRepository.GetListAsync(c => c.RestaurantId == restaurantId && c.IsActive);
        var items = (await _itemRepository.GetListAsync(i => i.RestaurantId == restaurantId && i.IsActive))
            .ToLookup(i => i.CategoryId);

        return new RestaurantMenuDto
        {
            Restaurant = ToSummary(restaurant, city, null),
            Categories = categories
                .OrderBy(c => c.SortOrder).ThenBy(c => c.CreationTime)
                .Select(c => new PublicMenuCategoryDto
                {
                    Id = c.Id,
                    Name = c.Name.Localize(),
                    Items = items[c.Id]
                        .OrderBy(i => i.SortOrder).ThenBy(i => i.CreationTime)
                        .Select(i => new PublicMenuItemDto
                        {
                            Id = i.Id,
                            Name = i.Name.Localize(),
                            Description = i.Description?.Localize(),
                            Price = i.Price,
                            ImageUrl = i.ImageUrl,
                            IsAvailable = i.IsAvailable
                        })
                        .ToList()
                })
                .Where(c => c.Items.Count > 0)
                .ToList()
        };
    }

    private RestaurantSummaryDto ToSummary(Restaurant restaurant, City city, Point? customerPoint)
    {
        var now = Clock.Now;
        var localNow = city.ToLocalTime(now);
        var isOpen = restaurant.IsOpenAt(localNow);
        var nextOpeningLocal = isOpen ? null : restaurant.GetNextOpeningAt(localNow);

        return new RestaurantSummaryDto
        {
            Id = restaurant.Id,
            Name = restaurant.Name.Localize(),
            AddressText = restaurant.AddressText,
            LogoUrl = restaurant.LogoUrl,
            MinOrderAmount = restaurant.MinOrderAmount,
            DefaultPrepMinutes = restaurant.DefaultPrepMinutes,
            AcceptsOrdersNow = restaurant.AcceptsOrdersAt(now, localNow),
            IsPausedNow = restaurant.IsPausedAt(now),
            NextOpeningAt = nextOpeningLocal.HasValue ? city.ToUtc(nextOpeningLocal.Value) : null,
            DistanceMeters = customerPoint == null
                ? null
                : Math.Round(GeoFactory.DistanceMeters(restaurant.Location, customerPoint))
        };
    }

    private async Task<City> GetCityAsync(Guid? cityId)
    {
        if (cityId.HasValue)
        {
            return await _cityRepository.GetAsync(cityId.Value);
        }

        var queryable = (await _cityRepository.GetQueryableAsync())
            .Where(c => c.IsActive)
            .OrderBy(c => c.CreationTime);

        return await AsyncExecuter.FirstOrDefaultAsync(queryable)
               ?? throw new EntityNotFoundException(typeof(City));
    }
}
