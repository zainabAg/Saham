using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;

namespace Saham.Entities.Restaurants;

/* DER-OPS-01 "tablet offline": the tablet pings every minute. The time is written straight to its
 * column, without touching the restaurant's ConcurrencyStamp or its change history — a heartbeat
 * must never conflict with the manager editing the menu or hours. */
public class TabletPresenceTracker : ITransientDependency
{
    private readonly IRepository<Restaurant, Guid> _restaurantRepository;
    private readonly IClock _clock;

    public TabletPresenceTracker(IRepository<Restaurant, Guid> restaurantRepository, IClock clock)
    {
        _restaurantRepository = restaurantRepository;
        _clock = clock;
    }

    public async Task MarkSeenAsync(Guid restaurantId)
    {
        var now = _clock.Now;
        await (await _restaurantRepository.GetDbSetAsync())
            .Where(r => r.Id == restaurantId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.TabletLastSeenAt, now));
    }
}
