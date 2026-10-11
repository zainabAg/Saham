using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Saham.Entities.Geography;
using Saham.Entities.Orders;
using Saham.Entities.Restaurants;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Threading;
using Volo.Abp.Timing;

namespace Saham.BackgroundWorkers;

/* Scheduled orders (DEC-02, BR-020/021): at release_at = scheduled_for − (prep + trip + margin) the
 * order goes to the restaurant (T-07) and follows the normal path. If the restaurant is not taking
 * orders then (outside hours, paused, suspended) or the address left the service area, the order is
 * cancelled with RESTAURANT_CLOSED_AT_RELEASE: a wallet payment is refunded in full automatically and
 * ops get a SCHEDULED_RESTAURANT_CLOSED incident (LedgerOrderEventHandler, IncidentOrderEventHandler).
 * A wallet order is captured when it is created, so only captured orders are ever released (BR-022). */
public class ScheduledReleaseWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const int BatchSize = 50;

    public ScheduledReleaseWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromSeconds(30).TotalMilliseconds;
    }

    protected override Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var services = workerContext.ServiceProvider;
        var now = services.GetRequiredService<IClock>().Now;

        return OrderSweeper.ProcessAsync(services, q => q.Where(o => o.Status == OrderStatus.Scheduled && o.ReleaseAt <= now),
            async (order, sp, guid) =>
            {
                var restaurant = await sp.GetRequiredService<IRepository<Restaurant, Guid>>().GetAsync(order.RestaurantId);
                var city = await sp.GetRequiredService<IRepository<City, Guid>>().GetAsync(order.CityId);
                if (restaurant.AcceptsOrdersAt(now, city.ToLocalTime(now)) && city.Serves(order.DeliveryLocation))
                {
                    order.Release(now, guid.Create);
                }
                else
                {
                    order.CancelBySystem(CancelReason.RestaurantClosedAtRelease, now, guid.Create);
                }
            }, Logger, BatchSize);
    }
}
