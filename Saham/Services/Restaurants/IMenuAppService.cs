using System;
using System.Threading.Tasks;
using Saham.Services.Dtos.Restaurants;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace Saham.Services.Restaurants;

public interface IMenuAppService : IApplicationService
{
    Task<ManagedMenuDto> GetAsync(Guid? restaurantId = null);

    Task<MenuCategoryDto> CreateCategoryAsync(CreateUpdateMenuCategoryDto input, Guid? restaurantId = null);

    Task<MenuCategoryDto> UpdateCategoryAsync(Guid id, CreateUpdateMenuCategoryDto input);

    Task<MenuItemDto> CreateItemAsync(CreateUpdateMenuItemDto input, Guid? restaurantId = null);

    Task<MenuItemDto> UpdateItemAsync(Guid id, CreateUpdateMenuItemDto input);

    Task<MenuItemDto> UploadItemImageAsync(Guid id, IRemoteStreamContent file);

    Task<MenuItemDto> ArchiveItemAsync(Guid id);

    Task<MenuItemDto> RestoreItemAsync(Guid id);

    Task<MenuItemDto> SetItemAvailabilityAsync(Guid id, SetItemAvailabilityDto input);
}
