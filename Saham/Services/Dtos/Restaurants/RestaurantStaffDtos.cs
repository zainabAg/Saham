using System;
using System.ComponentModel.DataAnnotations;
using Saham.Entities.Restaurants;
using Volo.Abp.Application.Dtos;

namespace Saham.Services.Dtos.Restaurants;

public class RestaurantStaffDto : EntityDto<Guid>
{
    public Guid UserId { get; set; }

    public string PhoneNumber { get; set; } = string.Empty;

    public string? Name { get; set; }

    public RestaurantStaffRole Role { get; set; }

    public bool IsActive { get; set; }
}

public class CreateRestaurantStaffDto
{
    [Required]
    [StringLength(SahamConsts.PhoneNumberMaxLength + 5)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(SahamConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    /* Kitchen or Cashier only: a manager cannot create another manager (SOD-05). */
    [Required]
    public RestaurantStaffRole Role { get; set; }
}

public class SetRestaurantStaffActiveDto
{
    public bool IsActive { get; set; }
}
