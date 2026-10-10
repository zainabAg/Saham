using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Identity;
using Saham.Permissions;
using Saham.Services.Dtos.Admin;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Saham.Services.Admin;

/* Owner manages company staff accounts: OPS, ACCOUNTANT, OWNER (SRS 5.2, user.role.manage).
 * Staff log in with phone + OTP like everyone else.
 * SOD-05: nobody edits their own roles, and at least one active owner always remains. */
[Authorize(SahamPermissions.Users.RoleManage)]
public class StaffUserAppService : SahamAppService, IStaffUserAppService
{
    private readonly IRepository<IdentityUser, Guid> _userRepository;
    private readonly IdentityUserManager _userManager;
    private readonly IdentityRoleManager _roleManager;
    private readonly PhoneUserManager _phoneUserManager;

    public StaffUserAppService(
        IRepository<IdentityUser, Guid> userRepository,
        IdentityUserManager userManager,
        IdentityRoleManager roleManager,
        PhoneUserManager phoneUserManager)
    {
        _userRepository = userRepository;
        _userManager = userManager;
        _roleManager = roleManager;
        _phoneUserManager = phoneUserManager;
    }

    public async Task<PagedResultDto<StaffUserDto>> GetListAsync(GetStaffUsersInput input)
    {
        var roleNames = input.Role.IsNullOrWhiteSpace()
            ? SahamRoles.CompanyStaff
            : new[] { CheckStaffRole(input.Role!) };
        var roleIds = await GetRoleIdsAsync(roleNames);

        var queryable = (await _userRepository.GetQueryableAsync())
            .Where(u => u.Roles.Any(r => roleIds.Contains(r.RoleId)))
            .WhereIf(!input.Filter.IsNullOrWhiteSpace(),
                u => u.PhoneNumber!.Contains(input.Filter!) || u.Name!.Contains(input.Filter!));

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var users = await AsyncExecuter.ToListAsync(queryable
            .OrderBy(input.Sorting.IsNullOrWhiteSpace() ? nameof(IdentityUser.CreationTime) + " desc" : input.Sorting)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        var items = new List<StaffUserDto>();
        foreach (var user in users)
        {
            items.Add(await MapAsync(user));
        }

        return new PagedResultDto<StaffUserDto>(totalCount, items);
    }

    public async Task<StaffUserDto> CreateAsync(CreateStaffUserDto input)
    {
        var roles = input.Roles.Select(CheckStaffRole).Distinct().ToList();
        var phoneNumber = PhoneNumberNormalizer.Normalize(input.PhoneNumber);

        var user = await _phoneUserManager.CreateAsync(phoneNumber, input.Name, roles);
        return await MapAsync(user);
    }

    public async Task<StaffUserDto> UpdateRolesAsync(Guid id, UpdateStaffRolesDto input)
    {
        CheckNotSelf(id);
        var user = await _userManager.GetByIdAsync(id);
        var newRoles = input.Roles.Select(CheckStaffRole).Distinct().ToList();

        var currentRoles = await _userManager.GetRolesAsync(user);
        var currentStaffRoles = currentRoles.Intersect(SahamRoles.CompanyStaff).ToList();

        if (currentStaffRoles.Contains(SahamRoles.Owner) && !newRoles.Contains(SahamRoles.Owner))
        {
            await CheckAnotherActiveOwnerExistsAsync(id);
        }

        var toRemove = currentStaffRoles.Except(newRoles).ToList();
        var toAdd = newRoles.Except(currentStaffRoles).ToList();
        if (toRemove.Count > 0)
        {
            (await _userManager.RemoveFromRolesAsync(user, toRemove)).CheckErrors();
        }
        if (toAdd.Count > 0)
        {
            (await _userManager.AddToRolesAsync(user, toAdd)).CheckErrors();
        }

        return await MapAsync(user);
    }

    public async Task SetActiveAsync(Guid id, SetUserActiveDto input)
    {
        CheckNotSelf(id);
        var user = await _userManager.GetByIdAsync(id);

        if (!input.IsActive && await _userManager.IsInRoleAsync(user, SahamRoles.Owner))
        {
            await CheckAnotherActiveOwnerExistsAsync(id);
        }

        user.SetIsActive(input.IsActive);
        (await _userManager.UpdateAsync(user)).CheckErrors();
    }

    private void CheckNotSelf(Guid id)
    {
        if (id == CurrentUser.Id)
        {
            throw new BusinessException(SahamErrorCodes.CannotChangeOwnRoles);
        }
    }

    private async Task CheckAnotherActiveOwnerExistsAsync(Guid excludedUserId)
    {
        var ownerRole = await _roleManager.FindByNameAsync(SahamRoles.Owner);
        var queryable = (await _userRepository.GetQueryableAsync())
            .Where(u => u.Id != excludedUserId && u.IsActive && u.Roles.Any(r => r.RoleId == ownerRole!.Id));

        if (!await AsyncExecuter.AnyAsync(queryable))
        {
            throw new BusinessException(SahamErrorCodes.LastOwnerCannotBeRemoved);
        }
    }

    private static string CheckStaffRole(string role)
    {
        var normalized = role.Trim().ToUpperInvariant();
        if (!SahamRoles.CompanyStaff.Contains(normalized))
        {
            throw new BusinessException(SahamErrorCodes.InvalidStaffRole).WithData("role", role);
        }

        return normalized;
    }

    private async Task<List<Guid>> GetRoleIdsAsync(IEnumerable<string> roleNames)
    {
        var ids = new List<Guid>();
        foreach (var roleName in roleNames)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role != null)
            {
                ids.Add(role.Id);
            }
        }

        return ids;
    }

    private async Task<StaffUserDto> MapAsync(IdentityUser user)
    {
        var dto = ObjectMapper.Map<IdentityUser, StaffUserDto>(user);
        dto.Roles = (await _userManager.GetRolesAsync(user)).ToList();
        return dto;
    }
}
