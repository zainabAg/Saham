using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Saham.Entities.Restaurants;
using Saham.Services.Dtos.Shared;
using Volo.Abp.Application.Dtos;

namespace Saham.Services.Dtos.Restaurants;

/* The full menu as the restaurant manages it, including archived items. */
public class ManagedMenuDto
{
    public Guid RestaurantId { get; set; }

    public List<MenuCategoryDto> Categories { get; set; } = new();
}

public class MenuCategoryDto : EntityDto<Guid>
{
    public I18nTextDto Name { get; set; } = new();

    public int SortOrder { get; set; }

    public bool IsActive { get; set; }

    public List<MenuItemDto> Items { get; set; } = new();
}

public class MenuItemDto : AuditedEntityDto<Guid>
{
    public Guid CategoryId { get; set; }

    public I18nTextDto Name { get; set; } = new();

    public I18nTextDto? Description { get; set; }

    public long Price { get; set; }

    public string? ImageUrl { get; set; }

    public int SortOrder { get; set; }

    public bool IsAvailable { get; set; }

    public bool IsActive { get; set; }
}

public class CreateUpdateMenuCategoryDto
{
    [Required]
    public I18nTextDto Name { get; set; } = new();

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

public class CreateUpdateMenuItemDto
{
    [Required]
    public Guid CategoryId { get; set; }

    [Required]
    public I18nTextDto Name { get; set; } = new();

    public I18nTextDto? Description { get; set; }

    [Range(1, long.MaxValue)]
    public long Price { get; set; }

    [StringLength(MenuItem.ImageUrlMaxLength)]
    public string? ImageUrl { get; set; }

    public int SortOrder { get; set; }
}

public class SetItemAvailabilityDto
{
    public bool IsAvailable { get; set; }
}
