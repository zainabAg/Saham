using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using NetTopologySuite.Geometries;
using Saham.Entities.Geography;
using Saham.Entities.Shared;
using Saham.Permissions;
using Saham.Services.Dtos.Admin;
using Saham.Services.Dtos.Shared;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Saham.Services.Admin;

/* Cities and their delivery polygon (DER-ADR-07). Reading is open to any signed-in user;
 * drawing the service area is an owner setting. */
[Authorize]
public class CityAppService : SahamAppService, ICityAppService
{
    private readonly IRepository<City, Guid> _cityRepository;

    public CityAppService(IRepository<City, Guid> cityRepository)
    {
        _cityRepository = cityRepository;
    }

    public async Task<ListResultDto<CityDto>> GetListAsync()
    {
        var cities = await _cityRepository.GetListAsync();
        return new ListResultDto<CityDto>(cities.OrderBy(c => c.CreationTime).Select(Map).ToList());
    }

    [Authorize(SahamPermissions.SystemSettings.Manage)]
    public async Task<CityDto> UpdateServiceAreaAsync(Guid id, UpdateServiceAreaDto input)
    {
        var city = await _cityRepository.GetAsync(id);

        city.SetServiceArea(input.Points.Count == 0
            ? null
            : GeoFactory.CreateArea(input.Points.Select(p => (p.Latitude, p.Longitude)).ToList()));

        await _cityRepository.UpdateAsync(city);
        return Map(city);
    }

    private CityDto Map(City city)
    {
        var dto = ObjectMapper.Map<City, CityDto>(city);
        dto.DisplayName = city.Name.Localize();
        dto.ServiceArea = ToRing(city.ServiceArea);
        return dto;
    }

    private static List<LocationDto> ToRing(MultiPolygon? area)
    {
        if (area == null || area.IsEmpty)
        {
            return new List<LocationDto>();
        }

        var polygon = (Polygon)area.GetGeometryN(0);
        return polygon.ExteriorRing.Coordinates
            .Select(c => new LocationDto { Latitude = c.Y, Longitude = c.X })
            .ToList();
    }
}
