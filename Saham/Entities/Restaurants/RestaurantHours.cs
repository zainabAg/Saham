using System;
using Volo.Abp.Domain.Entities;

namespace Saham.Entities.Restaurants;

/* One opening shift of a restaurant (SRS table restaurant_hours).
 * ClosesAt earlier than OpensAt means the shift ends the next day. */
public class RestaurantHours : Entity<Guid>
{
    public Guid RestaurantId { get; private set; }

    public DayOfWeek DayOfWeek { get; private set; }

    public TimeOnly OpensAt { get; private set; }

    public TimeOnly ClosesAt { get; private set; }

    protected RestaurantHours()
    {
    }

    internal RestaurantHours(Guid id, Guid restaurantId, DayOfWeek dayOfWeek, TimeOnly opensAt, TimeOnly closesAt)
        : base(id)
    {
        RestaurantId = restaurantId;
        DayOfWeek = dayOfWeek;
        OpensAt = opensAt;
        ClosesAt = closesAt;
    }
}
