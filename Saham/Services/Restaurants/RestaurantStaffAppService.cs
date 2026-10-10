using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Restaurants;
using Saham.Identity;
using Saham.Permissions;
using Saham.Services.Dtos.Restaurants;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Saham.Services.Restaurants;

/* The restaurant manager creates the kitchen and cashier accounts of their own
 * restaurant (DEC-12, restaurant.users.manage ◐). Scope comes from ICurrentScope:
 * restaurant staff act on their own restaurant; the owner passes restaurantId. */
[Authorize(SahamPermissions.Restaurants.UsersManage)]
public class RestaurantStaffAppService : SahamAppService, IRestaurantStaffAppService
{
    private readonly IRepository<RestaurantStaff, Guid> _staffRepository;
    private readonly IRepository<IdentityUser, Guid> _userRepository;
    private readonly IdentityUserManager _userManager;
    private readonly RestaurantStaffManager _restaurantStaffManager;
    private readonly ICurrentScope _currentScope;

    public RestaurantStaffAppService(
        IRepository<RestaurantStaff, Guid> staffRepository,
        IRepository<IdentityUser, Guid> userRepository,
        IdentityUserManager userManager,
        RestaurantStaffManager restaurantStaffManager,
        ICurrentScope currentScope)
    {
        _staffRepository = staffRepository;
        _userRepository = userRepository;
        _userManager = userManager;
        _restaurantStaffManager = restaurantStaffManager;
        _currentScope = currentScope;
    }

    public async Task<ListResultDto<RestaurantStaffDto>> GetListAsync(Guid? restaurantId = null)
    {
        restaurantId = await _currentScope.ResolveRestaurantIdAsync(restaurantId);

        var staff = await _staffRepository.GetListAsync(x => x.RestaurantId == restaurantId);
        var userIds = staff.Select(x => x.UserId).ToList();
        var users = (await _userRepository.GetListAsync(u => userIds.Contains(u.Id))).ToDictionary(u => u.Id);

        return new ListResultDto<RestaurantStaffDto>(staff
            .OrderBy(x => x.Role)
            .Select(x => Map(x, users.GetValueOrDefault(x.UserId)))
            .ToList());
    }

    public async Task<RestaurantStaffDto> CreateAsync(CreateRestaurantStaffDto input, Guid? restaurantId = null)
    {
        var scopedRestaurantId = await _currentScope.ResolveRestaurantIdAsync(restaurantId);
        var role = input.Role.ToRoleName();
        if (!SahamRoles.ManagerCreatable.Contains(role))
        {
            throw new BusinessException(SahamErrorCodes.InvalidRestaurantStaffRole);
        }

        var (staff, user) = await _restaurantStaffManager.CreateAsync(
            scopedRestaurantId,
            PhoneNumberNormalizer.Normalize(input.PhoneNumber),
            input.Name,
            input.Role);

        return Map(staff, user);
    }

    public async Task SetActiveAsync(Guid id, SetRestaurantStaffActiveDto input)
    {
        var staff = await _staffRepository.GetAsync(id);

        // Out-of-scope rows are reported as not found, never as forbidden.
        await _currentScope.ResolveRestaurantIdAsync(staff.RestaurantId);

        if (staff.Role == RestaurantStaffRole.Manager)
        {
            throw new BusinessException(SahamErrorCodes.InvalidRestaurantStaffRole);
        }

        if (input.IsActive)
        {
            staff.Activate();
        }
        else
        {
            staff.Deactivate();
        }
        await _staffRepository.UpdateAsync(staff);

        var user = await _userManager.GetByIdAsync(staff.UserId);
        user.SetIsActive(input.IsActive);
        (await _userManager.UpdateAsync(user)).CheckErrors();
    }

    private static RestaurantStaffDto Map(RestaurantStaff staff, IdentityUser? user)
    {
        return new RestaurantStaffDto
        {
            Id = staff.Id,
            UserId = staff.UserId,
            PhoneNumber = user?.PhoneNumber ?? string.Empty,
            Name = user?.Name,
            Role = staff.Role,
            IsActive = staff.IsActive
        };
    }
}
