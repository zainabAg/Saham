using System;
using System.Threading.Tasks;
using Saham.Identity;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Saham.Entities.Restaurants;

/* Creates a restaurant account: the phone user with its role + the link to the restaurant.
 * Who may create which role is decided by the caller:
 * the owner creates managers, a manager creates kitchen and cashier accounts (SOD-05). */
public class RestaurantStaffManager : DomainService
{
    private readonly IRepository<RestaurantStaff, Guid> _staffRepository;
    private readonly IRepository<Restaurant, Guid> _restaurantRepository;
    private readonly PhoneUserManager _phoneUserManager;

    public RestaurantStaffManager(
        IRepository<RestaurantStaff, Guid> staffRepository,
        IRepository<Restaurant, Guid> restaurantRepository,
        PhoneUserManager phoneUserManager)
    {
        _staffRepository = staffRepository;
        _restaurantRepository = restaurantRepository;
        _phoneUserManager = phoneUserManager;
    }

    public async Task<(RestaurantStaff Staff, IdentityUser User)> CreateAsync(
        Guid restaurantId,
        string normalizedPhone,
        string name,
        RestaurantStaffRole role)
    {
        // Throws EntityNotFoundException for an unknown restaurant.
        await _restaurantRepository.GetAsync(restaurantId, includeDetails: false);

        var user = await _phoneUserManager.CreateAsync(normalizedPhone, name, new[] { role.ToRoleName() });

        var staff = new RestaurantStaff(GuidGenerator.Create(), user.Id, restaurantId, role);
        await _staffRepository.InsertAsync(staff);

        return (staff, user);
    }
}
