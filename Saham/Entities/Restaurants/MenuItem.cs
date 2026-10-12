using System;
using Saham.Entities.Shared;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Restaurants;

/* A product on the menu (SRS table menu_items).
 * - IsAvailable: the one-tap "sold out" switch of the kitchen (REQ-RST-07, BR-003).
 * - IsActive=false: archived by the manager; never deleted because order lines reference it.
 * Prices are whole Iraqi dinars (D4). */
public class MenuItem : AuditedAggregateRoot<Guid>
{
    public const int ImageUrlMaxLength = 512;

    public Guid RestaurantId { get; private set; }

    public Guid CategoryId { get; private set; }

    public I18nText Name { get; private set; }

    public I18nText? Description { get; private set; }

    public long Price { get; private set; }

    public string? ImageUrl { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsAvailable { get; private set; }

    public bool IsActive { get; private set; }

    protected MenuItem()
    {
        Name = null!;
    }

    internal MenuItem(Guid id, Guid restaurantId, Guid categoryId, I18nText name, long price)
        : base(id)
    {
        RestaurantId = restaurantId;
        CategoryId = categoryId;
        Name = name;
        IsAvailable = true;
        IsActive = true;
        SetPrice(price);
    }

    /* Category ownership is checked by MenuManager before calling this. */
    internal void Update(Guid categoryId, I18nText name, I18nText? description, long price, string? imageUrl, int sortOrder)
    {
        CategoryId = categoryId;
        Name = name;
        Description = description;
        SetPrice(price);
        SetImage(imageUrl);
        SortOrder = sortOrder;
    }

    public void SetImage(string? imageUrl)
    {
        ImageUrl = imageUrl.IsNullOrWhiteSpace() ? null : Check.Length(imageUrl!.Trim(), nameof(imageUrl), ImageUrlMaxLength);
    }

    public void SetAvailability(bool isAvailable) => IsAvailable = isAvailable;

    public void Archive() => IsActive = false;

    public void Restore() => IsActive = true;

    /* Can be added to a cart / quote right now. */
    public bool IsOrderable => IsActive && IsAvailable;

    private void SetPrice(long price)
    {
        if (price <= 0)
        {
            throw new BusinessException(SahamErrorCodes.InvalidAmount);
        }

        Price = price;
    }
}
