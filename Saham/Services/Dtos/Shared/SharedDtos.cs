using System.ComponentModel.DataAnnotations;
using Saham.Entities.Shared;

namespace Saham.Services.Dtos.Shared;

/* Translatable text: Arabic required, other languages optional (TD-05). */
public class I18nTextDto
{
    [Required]
    [StringLength(I18nText.MaxLength)]
    public string Ar { get; set; } = string.Empty;

    [StringLength(I18nText.MaxLength)]
    public string? En { get; set; }

    [StringLength(I18nText.MaxLength)]
    public string? Fa { get; set; }

    [StringLength(I18nText.MaxLength)]
    public string? Ur { get; set; }

    public I18nText ToValue() => new(Ar, En, Fa, Ur);
}

public class LocationDto
{
    [Range(-90, 90)]
    public double Latitude { get; set; }

    [Range(-180, 180)]
    public double Longitude { get; set; }
}
