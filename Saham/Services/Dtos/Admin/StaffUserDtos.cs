using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace Saham.Services.Dtos.Admin;

public class StaffUserDto : EntityDto<Guid>
{
    public string PhoneNumber { get; set; } = string.Empty;

    public string? Name { get; set; }

    public bool IsActive { get; set; }

    public List<string> Roles { get; set; } = new();

    public DateTime CreationTime { get; set; }
}

public class GetStaffUsersInput : PagedAndSortedResultRequestDto
{
    /* OPS, ACCOUNTANT or OWNER; empty = all company staff. */
    public string? Role { get; set; }

    public string? Filter { get; set; }
}

public class CreateStaffUserDto
{
    [Required]
    [StringLength(SahamConsts.PhoneNumberMaxLength + 5)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(SahamConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    public List<string> Roles { get; set; } = new();
}

public class UpdateStaffRolesDto
{
    [Required]
    [MinLength(1)]
    public List<string> Roles { get; set; } = new();
}

public class SetUserActiveDto
{
    public bool IsActive { get; set; }
}
