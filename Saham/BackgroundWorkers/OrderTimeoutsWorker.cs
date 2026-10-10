using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Saham.Entities.Geography;
using Saham.Entities.Orders;
using Saham.Entities.Restaurants;
using Saham.Settings;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Settings;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Saham.BackgroundWorkers;

/* Order timers, swept from the database so nothing is lost on restart (SRS 14.7):
 *   1. BR-023: no restaurant answer within N minutes → cancel (RESTAURANT_TIMEOUT);
 *   2. BR-047 (MVP): no customer decision on an unavailable item → continue without it;
 *   3. BR-021: scheduled order at its release time → send to the restaurant, or cancel
 *      (RESTAURANT_CLOSED_AT_RELEASE) if the restaurant is not taking orders.
 * Each order is handled in its own transaction; a concurrent change (e.g. the restaurant
 * accepting at the same second) wins and the timer simply skips that order. */
public class OrderTimeoutsWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const int BatchSize = 50;

    public OrderTimeoutsWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromSeconds(15).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var services = workerContext.ServiceProvider;
        var clock = services.GetRequiredService<IClock>();
        var settings = services.GetRequiredService<ISettingProvider>();
        var now = clock.Now;

        var timeoutMinutes = await settings.GetAsync<int>(SahamSettings.Orders.RestaurantAutoCancelMinutes);
        var placedBefore = now.AddMinutes(-timeoutMinutes);

        await ProcessAsync(services, q => q.Where(o => o.Status == OrderStatus.Placed && o.PlacedAt <= placedBefore),
            (order, _, guid) =>
            {
                order.CancelBySystem(CancelReason.RestaurantTimeout, now, guid.Create);
                return Task.CompletedTask;
            });

        await ProcessAsync(services, q => q.Where(o => o.HoldReason == OrderHoldReason.ItemUnavailable && o.ItemDecisionDeadlineAt <= now),
            (order, _, guid) =>
            {
                order.ResolveItemDecision(ItemDecision.ContinueWithout, ActorType.System, null, now, guid.Create);
                return Task.CompletedTask;
            });

        await ProcessAsync(services, q => q.Where(o => o.Status == OrderStatus.Scheduled && o.ReleaseAt <= now),
            async (order, sp, guid) =>
            {
                var restaurant = await sp.GetRequiredService<IRepository<Restaurant, Guid>>().GetAsync(order.RestaurantId);
                var city = await sp.GetRequiredService<IRepository<City, Guid>>().GetAsync(order.CityId);
                if (restaurant.AcceptsOrdersAt(now, city.ToLocalTime(now)))
                {
                    order.Release(now, guid.Create);
                }
                else
                {
                    order.CancelBySystem(CancelReason.RestaurantClosedAtRelease, now, guid.Create);
                }
            });
    }

    private async Task ProcessAsync(
        IServiceProvider services,
        Func<IQueryable<Order>, IQueryable<Order>> filter,
        Func<Order, IServiceProvider, IGuidGenerator, Task> action)
    {
        var repository = services.GetRequiredService<IRepository<Order, Guid>>();
        var unitOfWorkManager = services.GetRequiredService<IUnitOfWorkManager>();
        var guidGenerator = services.GetRequiredService<IGuidGenerator>();
        var asyncExecuter = services.GetRequiredService<Volo.Abp.Linq.IAsyncQueryableExecuter>();

        List<Guid> ids;
        using (var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            ids = await asyncExecuter.ToListAsync(filter(await repository.GetQueryableAsync())
                .OrderBy(o => o.CreationTime).Select(o => o.Id).Take(BatchSize));
            await uow.CompleteAsync();
        }

        foreach (var id in ids)
        {
            try
            {
                using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
                var order = await repository.GetAsync(id);

                // Re-check on the fresh row: e.g. a restaurant that accepted a second ago must not be
                // cancelled by the timeout (Accepted → Cancelled is itself a legal transition).
                if (!filter(new[] { order }.AsQueryable()).Any())
                {
                    continue;
                }

                await action(order, services, guidGenerator);
                await repository.UpdateAsync(order);
                await uow.CompleteAsync();
            }
            catch (AbpDbConcurrencyException)
            {
                // Someone else changed the order first; their change wins.
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Order timer failed for order {OrderId}", id);
            }
        }
    }
}
