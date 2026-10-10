using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Saham.Services.Dtos.Shared;
using Volo.Abp.Application.Dtos;

namespace Saham.Services.Dtos.Admin;

public class CityDto : EntityDto<Guid>
{
    public I18nTextDto Name { get; set; } = new();

    public string DisplayName { get; set; } = string.Empty;

    public string TimeZone { get; set; } = string.Empty;

    public LocationDto Center { get; set; } = new();

    /* Outer ring of the service area polygon; empty = no polygon yet (whole city accepted). */
    public List<LocationDto> ServiceArea { get; set; } = new();

    public bool IsActive { get; set; }
}

public class UpdateServiceAreaDto
{
    /* At least 3 points; send an empty list to remove the polygon. */
    [Required]
    public List<LocationDto> Points { get; set; } = new();
}
