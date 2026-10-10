using AutoMapper;
using NetTopologySuite.Geometries;
using Saham.Entities.Geography;
using Saham.Entities.Notifications;
using Saham.Entities.Pricing;
using Saham.Entities.Drivers;
using Saham.Entities.Support;
using Saham.Services.Dtos.Drivers;
using Saham.Entities.Restaurants;
using Saham.Entities.Shared;
using Saham.Services.Dtos.Customers;
using Saham.Services.Dtos.Restaurants;
using Saham.Services.Dtos.Shared;
using Saham.Services.Dtos.Admin;
using Volo.Abp.AutoMapper;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Saham.ObjectMapping;

public class SahamAutoMapperProfile : Profile
{
    public SahamAutoMapperProfile()
    {
        // Admin
        CreateMap<IdentityUser, StaffUserDto>()
            .ForMember(d => d.PhoneNumber, o => o.MapFrom(s => s.PhoneNumber ?? string.Empty))
            .Ignore(d => d.Roles);
        CreateMap<SmsLog, SmsLogDto>();
        CreateMap<City, CityDto>()
            .Ignore(d => d.DisplayName)
            .Ignore(d => d.ServiceArea);
        CreateMap<Restaurant, RestaurantDto>();
        CreateMap<PricingPlan, PricingPlanDto>();
        CreateMap<PricingTier, PricingTierDto>();

        // Shared value objects
        CreateMap<I18nText, I18nTextDto>();
        CreateMap<Point, LocationDto>()
            .ConvertUsing(p => new LocationDto { Latitude = p.Y, Longitude = p.X });

        // Restaurants (staff screens)
        CreateMap<Restaurant, RestaurantProfileDto>()
            .Ignore(d => d.IsOpenNow)
            .Ignore(d => d.AcceptsOrdersNow);
        CreateMap<RestaurantHours, OpeningShiftDto>();
        CreateMap<MenuCategory, MenuCategoryDto>()
            .Ignore(d => d.Items);
        CreateMap<MenuItem, MenuItemDto>();

        // Customers
        CreateMap<CustomerAddress, AddressDto>();

        // Drivers
        CreateMap<Driver, DriverProfileDto>()
            .Ignore(d => d.Name)
            .Ignore(d => d.PhoneNumber)
            .Ignore(d => d.Documents);
        CreateMap<Driver, DriverAdminDto>()
            .Ignore(d => d.Name)
            .Ignore(d => d.PhoneNumber)
            .Ignore(d => d.LastLocation)
            .Ignore(d => d.Documents);
        CreateMap<DriverDocument, DriverDocumentDto>();
        CreateMap<Incident, IncidentDto>();
        /* Create your AutoMapper object mappings here */
    }
}