using System;
using Saham.Entities.Shared;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Restaurants;

/* A menu section of one restaurant (SRS table menu_categories). */
public class MenuCategory : AuditedAggregateRoot<Guid>
{
    public Guid RestaurantId { get; private set; }

    public I18nText Name { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsActive { get; private set; }

    protected MenuCategory()
    {
        Name = null!;
    }

    public MenuCategory(Guid id, Guid restaurantId, I18nText name, int sortOrder)
        : base(id)
    {
        RestaurantId = restaurantId;
        Name = name;
        SortOrder = sortOrder;
        IsActive = true;
    }

    public void Update(I18nText name, int sortOrder, bool isActive)
    {
        Name = name;
        SortOrder = sortOrder;
        IsActive = isActive;
    }
}
