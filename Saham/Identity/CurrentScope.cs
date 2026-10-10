using System;
using System.Threading.Tasks;
using Saham.Entities.Restaurants;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.DependencyInjection;
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
}

public class CurrentScope : ICurrentScope, IScopedDependency
{
    private readonly ICurrentUser _currentUser;
    private readonly IRepository<RestaurantStaff, Guid> _restaurantStaffRepository;
    private RestaurantStaff? _restaurantStaff;

    public CurrentScope(ICurrentUser currentUser, IRepository<RestaurantStaff, Guid> restaurantStaffRepository)
    {
        _currentUser = currentUser;
        _restaurantStaffRepository = restaurantStaffRepository;
    }

    public Guid UserId => _currentUser.GetId();

    public async Task<RestaurantStaff> GetRestaurantStaffAsync()
    {
        if (_restaurantStaff != null)
        {
            return _restaurantStaff;
        }

        var userId = UserId;
        _restaurantStaff = await _restaurantStaffRepository.FindAsync(x => x.UserId == userId && x.IsActive);
        if (_restaurantStaff == null)
        {
            throw new AbpAuthorizationException(code: SahamErrorCodes.NotRestaurantStaff);
        }

        return _restaurantStaff;
    }

    public async Task<Guid> GetRestaurantIdAsync()
    {
        return (await GetRestaurantStaffAsync()).RestaurantId;
    }
}
