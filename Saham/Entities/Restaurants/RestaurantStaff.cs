using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Restaurants;

/* Links a user to the restaurant they work for, with one of the three
 * restaurant roles (DEC-12). A user belongs to at most one restaurant.
 * The FK to Restaurant is added together with the Restaurant entity (phase 2). */
public class RestaurantStaff : FullAuditedAggregateRoot<Guid>
{
    public Guid UserId { get; private set; }

    public Guid RestaurantId { get; private set; }

    public RestaurantStaffRole Role { get; private set; }

    public bool IsActive { get; private set; }

    protected RestaurantStaff()
    {
    }

    public RestaurantStaff(Guid id, Guid userId, Guid restaurantId, RestaurantStaffRole role)
        : base(id)
    {
        UserId = userId;
        RestaurantId = restaurantId;
        Role = role;
        IsActive = true;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
