using System;
using System.Threading.Tasks;
using Saham.Services.Dtos.Restaurants;
using Volo.Abp.Application.Services;

namespace Saham.Services.Restaurants;

public interface IRestaurantProfileAppService : IApplicationService
{
    Task<RestaurantProfileDto> GetAsync(Guid? restaurantId = null);

    Task<RestaurantProfileDto> UpdateHoursAsync(UpdateOpeningHoursDto input, Guid? restaurantId = null);

    Task<RestaurantProfileDto> UpdateSettingsAsync(UpdateRestaurantSettingsDto input, Guid? restaurantId = null);

    Task<RestaurantProfileDto> PauseAsync(PauseRestaurantDto input, Guid? restaurantId = null);

    Task<RestaurantProfileDto> ResumeAsync(Guid? restaurantId = null);

    Task HeartbeatAsync();
}
