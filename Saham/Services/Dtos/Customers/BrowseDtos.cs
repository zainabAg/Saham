using System;
using System.Collections.Generic;
using Volo.Abp.Application.Dtos;

namespace Saham.Services.Dtos.Customers;

public class BrowseRestaurantsInput
{
    /* Defaults to the first active city (Karbala). */
    public Guid? CityId { get; set; }

    public string? Filter { get; set; }

    /* Optional customer position, to show and sort by distance. */
    public double? Latitude { get; set; }

    public double? Longitude { get; set; }
}

/* A restaurant card on the customer's home screen (REQ-CUS-01). */
public class RestaurantSummaryDto : EntityDto<Guid>
{
    public string Name { get; set; } = string.Empty;

    public string AddressText { get; set; } = string.Empty;

    public string? LogoUrl { get; set; }

    public long MinOrderAmount { get; set; }

    public int DefaultPrepMinutes { get; set; }

    /* true = shown in the "order now" list; false = shown as "closed — opens at …". */
    public bool AcceptsOrdersNow { get; set; }

    public bool IsPausedNow { get; set; }

    /* UTC; filled when the restaurant is closed by its hours. */
    public DateTime? NextOpeningAt { get; set; }

    public double? DistanceMeters { get; set; }
}

/* The menu as the customer sees it (REQ-CUS-02): active sections and items only;
 * an item switched off by the kitchen is returned with IsAvailable=false (shown grey). */
public class RestaurantMenuDto
{
    public RestaurantSummaryDto Restaurant { get; set; } = new();

    public List<PublicMenuCategoryDto> Categories { get; set; } = new();
}

public class PublicMenuCategoryDto : EntityDto<Guid>
{
    public string Name { get; set; } = string.Empty;

    public List<PublicMenuItemDto> Items { get; set; } = new();
}

public class PublicMenuItemDto : EntityDto<Guid>
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public long Price { get; set; }

    public string? ImageUrl { get; set; }

    public bool IsAvailable { get; set; }
}
