using System;
using System.Threading.Tasks;
using Saham.Entities.Shared;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Saham.Entities.Restaurants;

/* Keeps every menu item inside a category of the same restaurant
 * (also enforced by a composite foreign key, SRS 13.4 menu_items). */
public class MenuManager : DomainService
{
    private readonly IRepository<MenuCategory, Guid> _categoryRepository;

    public MenuManager(IRepository<MenuCategory, Guid> categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    public async Task<MenuItem> CreateItemAsync(
        Guid restaurantId,
        Guid categoryId,
        I18nText name,
        I18nText? description,
        long price,
        string? imageUrl,
        int sortOrder)
    {
        await CheckCategoryAsync(restaurantId, categoryId);

        var item = new MenuItem(GuidGenerator.Create(), restaurantId, categoryId, name, price);
        item.Update(categoryId, name, description, price, imageUrl, sortOrder);
        return item;
    }

    public async Task UpdateItemAsync(
        MenuItem item,
        Guid categoryId,
        I18nText name,
        I18nText? description,
        long price,
        string? imageUrl,
        int sortOrder)
    {
        await CheckCategoryAsync(item.RestaurantId, categoryId);
        item.Update(categoryId, name, description, price, imageUrl, sortOrder);
    }

    private async Task CheckCategoryAsync(Guid restaurantId, Guid categoryId)
    {
        var category = await _categoryRepository.FindAsync(categoryId);
        if (category == null || category.RestaurantId != restaurantId)
        {
            throw new BusinessException(SahamErrorCodes.MenuCategoryNotInRestaurant);
        }
    }
}
