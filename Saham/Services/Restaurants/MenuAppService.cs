using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Restaurants;
using Saham.Identity;
using Saham.Permissions;
using Saham.Services.Dtos.Restaurants;
using Volo.Abp.Domain.Repositories;

namespace Saham.Services.Restaurants;

/* Menu management by the restaurant itself (REQ-RST-06/07, BR-050):
 * - products, prices, photos, sections, archiving: manager (and owner) — menu.manage;
 * - one-tap "sold out" switch: manager, kitchen, ops, owner — menu.toggle_availability.
 * Restaurant staff act on their own restaurant; company staff pass restaurantId. */
[Authorize]
public class MenuAppService : SahamAppService, IMenuAppService
{
    private readonly IRepository<MenuCategory, Guid> _categoryRepository;
    private readonly IRepository<MenuItem, Guid> _itemRepository;
    private readonly IRepository<Restaurant, Guid> _restaurantRepository;
    private readonly MenuManager _menuManager;
    private readonly ICurrentScope _currentScope;

    public MenuAppService(
        IRepository<MenuCategory, Guid> categoryRepository,
        IRepository<MenuItem, Guid> itemRepository,
        IRepository<Restaurant, Guid> restaurantRepository,
        MenuManager menuManager,
        ICurrentScope currentScope)
    {
        _categoryRepository = categoryRepository;
        _itemRepository = itemRepository;
        _restaurantRepository = restaurantRepository;
        _menuManager = menuManager;
        _currentScope = currentScope;
    }

    [Authorize(SahamPermissions.Menu.ToggleAvailability)]
    public async Task<ManagedMenuDto> GetAsync(Guid? restaurantId = null)
    {
        var id = await ResolveRestaurantAsync(restaurantId);

        var categories = await _categoryRepository.GetListAsync(x => x.RestaurantId == id);
        var items = await _itemRepository.GetListAsync(x => x.RestaurantId == id);
        var itemsByCategory = items.ToLookup(x => x.CategoryId);

        return new ManagedMenuDto
        {
            RestaurantId = id,
            Categories = categories
                .OrderBy(c => c.SortOrder).ThenBy(c => c.CreationTime)
                .Select(c =>
                {
                    var dto = ObjectMapper.Map<MenuCategory, MenuCategoryDto>(c);
                    dto.Items = ObjectMapper.Map<List<MenuItem>, List<MenuItemDto>>(itemsByCategory[c.Id]
                        .OrderBy(i => i.SortOrder).ThenBy(i => i.CreationTime)
                        .ToList());
                    return dto;
                })
                .ToList()
        };
    }

    [Authorize(SahamPermissions.Menu.Manage)]
    public async Task<MenuCategoryDto> CreateCategoryAsync(CreateUpdateMenuCategoryDto input, Guid? restaurantId = null)
    {
        var id = await ResolveRestaurantAsync(restaurantId);

        var category = new MenuCategory(GuidGenerator.Create(), id, input.Name.ToValue(), input.SortOrder);
        if (!input.IsActive)
        {
            category.Update(category.Name, category.SortOrder, false);
        }

        await _categoryRepository.InsertAsync(category);
        return ObjectMapper.Map<MenuCategory, MenuCategoryDto>(category);
    }

    [Authorize(SahamPermissions.Menu.Manage)]
    public async Task<MenuCategoryDto> UpdateCategoryAsync(Guid id, CreateUpdateMenuCategoryDto input)
    {
        var category = await _categoryRepository.GetAsync(id);
        await _currentScope.ResolveRestaurantIdAsync(category.RestaurantId);

        category.Update(input.Name.ToValue(), input.SortOrder, input.IsActive);

        await _categoryRepository.UpdateAsync(category);
        return ObjectMapper.Map<MenuCategory, MenuCategoryDto>(category);
    }

    [Authorize(SahamPermissions.Menu.Manage)]
    public async Task<MenuItemDto> CreateItemAsync(CreateUpdateMenuItemDto input, Guid? restaurantId = null)
    {
        var id = await ResolveRestaurantAsync(restaurantId);

        var item = await _menuManager.CreateItemAsync(
            id,
            input.CategoryId,
            input.Name.ToValue(),
            input.Description?.ToValue(),
            input.Price,
            input.ImageUrl,
            input.SortOrder);

        await _itemRepository.InsertAsync(item);
        return ObjectMapper.Map<MenuItem, MenuItemDto>(item);
    }

    [Authorize(SahamPermissions.Menu.Manage)]
    public async Task<MenuItemDto> UpdateItemAsync(Guid id, CreateUpdateMenuItemDto input)
    {
        var item = await GetItemInScopeAsync(id);

        await _menuManager.UpdateItemAsync(
            item,
            input.CategoryId,
            input.Name.ToValue(),
            input.Description?.ToValue(),
            input.Price,
            input.ImageUrl,
            input.SortOrder);

        await _itemRepository.UpdateAsync(item);
        return ObjectMapper.Map<MenuItem, MenuItemDto>(item);
    }

    [Authorize(SahamPermissions.Menu.Manage)]
    public async Task<MenuItemDto> ArchiveItemAsync(Guid id)
    {
        var item = await GetItemInScopeAsync(id);
        item.Archive();

        await _itemRepository.UpdateAsync(item);
        return ObjectMapper.Map<MenuItem, MenuItemDto>(item);
    }

    [Authorize(SahamPermissions.Menu.Manage)]
    public async Task<MenuItemDto> RestoreItemAsync(Guid id)
    {
        var item = await GetItemInScopeAsync(id);
        item.Restore();

        await _itemRepository.UpdateAsync(item);
        return ObjectMapper.Map<MenuItem, MenuItemDto>(item);
    }

    [Authorize(SahamPermissions.Menu.ToggleAvailability)]
    public async Task<MenuItemDto> SetItemAvailabilityAsync(Guid id, SetItemAvailabilityDto input)
    {
        var item = await GetItemInScopeAsync(id);
        item.SetAvailability(input.IsAvailable);

        await _itemRepository.UpdateAsync(item);
        return ObjectMapper.Map<MenuItem, MenuItemDto>(item);
    }

    private async Task<Guid> ResolveRestaurantAsync(Guid? restaurantId)
    {
        var id = await _currentScope.ResolveRestaurantIdAsync(restaurantId);
        await _restaurantRepository.GetAsync(id, includeDetails: false); // 404 for an unknown restaurant
        return id;
    }

    /* Items of another restaurant are reported as not found. */
    private async Task<MenuItem> GetItemInScopeAsync(Guid id)
    {
        var item = await _itemRepository.GetAsync(id);
        await _currentScope.ResolveRestaurantIdAsync(item.RestaurantId);
        return item;
    }
}
