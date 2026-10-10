using System;
using System.ComponentModel.DataAnnotations;
using Saham.Entities.Geography;
using Saham.Services.Dtos.Shared;
using Volo.Abp.Application.Dtos;

namespace Saham.Services.Dtos.Customers;

public class AddressDto : AuditedEntityDto<Guid>
{
    public Guid CityId { get; set; }

    public AddressLabel Label { get; set; }

    public string? CustomLabel { get; set; }

    public LocationDto Location { get; set; } = new();

    public string Description { get; set; } = string.Empty;

    public string? RecipientName { get; set; }

    public string? RecipientPhone { get; set; }
}

/* REQ-ADR-03: label, pin, landmark description, optional recipient — no street fields. */
public class CreateUpdateAddressDto
{
    /* Defaults to the first active city. */
    public Guid? CityId { get; set; }

    public AddressLabel Label { get; set; }

    [StringLength(CustomerAddress.CustomLabelMaxLength)]
    public string? CustomLabel { get; set; }

    [Required]
    public LocationDto Location { get; set; } = new();

    [Required]
    [StringLength(CustomerAddress.DescriptionMaxLength)]
    public string Description { get; set; } = string.Empty;

    [StringLength(SahamConsts.NameMaxLength)]
    public string? RecipientName { get; set; }

    [StringLength(SahamConsts.PhoneNumberMaxLength + 5)]
    public string? RecipientPhone { get; set; }
}
