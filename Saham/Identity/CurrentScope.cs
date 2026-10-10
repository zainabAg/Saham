using System;
using System.Threading.Tasks;
using Saham.Entities.Drivers;
using Saham.Entities.Restaurants;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace Saham.Identity;

/* Data scope of the current user (SRS 5.1): a permission alone is not enough,
 * a restaurant user only sees their restaurant, a driver only their tasks,
 * a customer only their orders. App services filter every query with it. */
public interface ICurrentScope
{
    Guid UserId { get; }

    Task<RestaurantStaff> GetRestaurantStaffAsync();

    Task<Guid> GetRestaurantIdAsync();

    /* Restaurant staff always act on their own restaurant (another requested id is "not found").
     * Company staff (owner, ops) have no restaurant link and must name the restaurant. */
    Task<Guid> ResolveRestaurantIdAsync(Guid? requestedRestaurantId);

    Task<bool> IsRestaurantStaffAsync();

    /* The driver profile of the current user (a driver only ever acts as himself). */
    Task<Driver> GetDriverAsync();
}

public class CurrentScope : ICurrentScope, IScopedDependency
{
    private readonly ICurrentUser _currentUser;
    private readonly IRepository<RestaurantStaff, Guid> _restaurantStaffRepository;
    private readonly IRepository<Driver, Guid> _driverRepository;
    private RestaurantStaff? _restaurantStaff;

    public CurrentScope(
        ICurrentUser currentUser,
        IRepository<RestaurantStaff, Guid> restaurantStaffRepository,
        IRepository<Driver, Guid> driverRepository)
    {
        _currentUser = currentUser;
        _restaurantStaffRepository = restaurantStaffRepository;
        _driverRepository = driverRepository;
    }

    public async Task<Driver> GetDriverAsync()
    {
        return await _driverRepository.FindAsync(UserId)
               ?? throw new AbpAuthorizationException(code: SahamErrorCodes.NotADriver);
    }

    public Guid UserId => _currentUser.GetId();

    public async Task<RestaurantStaff> GetRestaurantStaffAsync()
    {
        if (await FindRestaurantStaffAsync() == null)
        {
            throw new AbpAuthorizationException(code: SahamErrorCodes.NotRestaurantStaff);
        }

        return _restaurantStaff!;
    }

    public async Task<Guid> GetRestaurantIdAsync()
    {
        return (await GetRestaurantStaffAsync()).RestaurantId;
    }

    public async Task<Guid> ResolveRestaurantIdAsync(Guid? requestedRestaurantId)
    {
        var staff = await FindRestaurantStaffAsync();
        if (staff != null)
        {
            if (requestedRestaurantId.HasValue && requestedRestaurantId.Value != staff.RestaurantId)
            {
                throw new EntityNotFoundException(typeof(Restaurant), requestedRestaurantId.Value);
            }

            return staff.RestaurantId;
        }

        if (!requestedRestaurantId.HasValue)
        {
            throw new BusinessException(SahamErrorCodes.RestaurantIdRequired);
        }

        return requestedRestaurantId.Value;
    }

    public async Task<bool> IsRestaurantStaffAsync() => await FindRestaurantStaffAsync() != null;

    private async Task<RestaurantStaff?> FindRestaurantStaffAsync()
    {
        if (_restaurantStaff == null)
        {
            var userId = UserId;
            _restaurantStaff = await _restaurantStaffRepository.FindAsync(x => x.UserId == userId && x.IsActive);
        }

        return _restaurantStaff;
    }
}
