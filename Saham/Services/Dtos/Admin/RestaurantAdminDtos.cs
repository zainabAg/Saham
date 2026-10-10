using System;
using System.ComponentModel.DataAnnotations;
using Saham.Entities.Restaurants;
using Saham.Services.Dtos.Shared;
using Volo.Abp.Application.Dtos;

namespace Saham.Services.Dtos.Admin;

/* Full restaurant record for the owner's admin panel. */
public class RestaurantDto : FullAuditedEntityDto<Guid>
{
    public Guid CityId { get; set; }

    public I18nTextDto Name { get; set; } = new();

    public string Phone { get; set; } = string.Empty;

    public LocationDto Location { get; set; } = new();

    public string AddressText { get; set; } = string.Empty;

    public string? LogoUrl { get; set; }

    public RestaurantStatus Status { get; set; }

    public bool IsPaused { get; set; }

    public string? PausedReason { get; set; }

    public DateTime? PausedUntil { get; set; }

    public int DefaultPrepMinutes { get; set; }

    public long MinOrderAmount { get; set; }

    public int CommissionRateBp { get; set; }

    public DateTime? TabletLastSeenAt { get; set; }
}

public class GetRestaurantsInput : PagedAndSortedResultRequestDto
{
    public string? Filter { get; set; }

    public RestaurantStatus? Status { get; set; }

    public Guid? CityId { get; set; }
}

public class CreateRestaurantDto
{
    [Required]
    public Guid CityId { get; set; }

    [Required]
    public I18nTextDto Name { get; set; } = new();

    [Required]
    [StringLength(Restaurant.PhoneMaxLength + 5)]
    public string Phone { get; set; } = string.Empty;

    [Required]
    public LocationDto Location { get; set; } = new();

    [Required]
    [StringLength(Restaurant.AddressTextMaxLength)]
    public string AddressText { get; set; } = string.Empty;

    [StringLength(Restaurant.LogoUrlMaxLength)]
    public string? LogoUrl { get; set; }

    /* Basis points: 1000 = 10% (DEC-05 default). */
    [Range(0, Restaurant.MaxCommissionRateBp)]
    public int CommissionRateBp { get; set; } = Restaurant.DefaultCommissionRateBp;
}

public class UpdateRestaurantDto
{
    [Required]
    public I18nTextDto Name { get; set; } = new();

    [Required]
    [StringLength(Restaurant.PhoneMaxLength + 5)]
    public string Phone { get; set; } = string.Empty;

    [Required]
    public LocationDto Location { get; set; } = new();

    [Required]
    [StringLength(Restaurant.AddressTextMaxLength)]
    public string AddressText { get; set; } = string.Empty;

    [StringLength(Restaurant.LogoUrlMaxLength)]
    public string? LogoUrl { get; set; }
}

public class SetCommissionRateDto
{
    [Range(0, Restaurant.MaxCommissionRateBp)]
    public int CommissionRateBp { get; set; }
}

public class CreateRestaurantManagerDto
{
    [Required]
    [StringLength(SahamConsts.PhoneNumberMaxLength + 5)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(SahamConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;
}
