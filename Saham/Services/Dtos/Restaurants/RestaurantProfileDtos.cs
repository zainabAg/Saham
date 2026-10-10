using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Saham.Entities.Restaurants;
using Saham.Services.Dtos.Shared;
using Volo.Abp.Application.Dtos;

namespace Saham.Services.Dtos.Restaurants;

/* What the restaurant's own screens show about the restaurant. */
public class RestaurantProfileDto : EntityDto<Guid>
{
    public I18nTextDto Name { get; set; } = new();

    public string AddressText { get; set; } = string.Empty;

    public string? LogoUrl { get; set; }

    public RestaurantStatus Status { get; set; }

    public bool IsPaused { get; set; }

    public string? PausedReason { get; set; }

    public DateTime? PausedUntil { get; set; }

    public bool IsOpenNow { get; set; }

    public bool AcceptsOrdersNow { get; set; }

    public int DefaultPrepMinutes { get; set; }

    public long MinOrderAmount { get; set; }

    public int CommissionRateBp { get; set; }

    public List<OpeningShiftDto> Hours { get; set; } = new();
}

/* One opening shift; ClosesAt earlier than OpensAt = closes after midnight (18:00 → 02:00). */
public class OpeningShiftDto
{
    public DayOfWeek DayOfWeek { get; set; }

    public TimeOnly OpensAt { get; set; }

    public TimeOnly ClosesAt { get; set; }
}

public class UpdateOpeningHoursDto
{
    [Required]
    public List<OpeningShiftDto> Shifts { get; set; } = new();
}

public class UpdateRestaurantSettingsDto
{
    [Range(0, long.MaxValue)]
    public long MinOrderAmount { get; set; }

    [Range(1, 1440)]
    public int DefaultPrepMinutes { get; set; }
}

public class PauseRestaurantDto
{
    [StringLength(Restaurant.PausedReasonMaxLength)]
    public string? Reason { get; set; }

    /* Optional automatic reopening time (UTC). */
    public DateTime? Until { get; set; }
}
