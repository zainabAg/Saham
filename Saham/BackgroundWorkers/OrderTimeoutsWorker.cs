using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Saham.Entities.Geography;
using Saham.Entities.Notifications;
using Saham.Entities.Support;
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
 *   2. BR-047 (MVP): no customer decision on an unavailable item → continue without it
 *      (and an ITEM_DECISION_TIMEOUT incident so ops follow up);
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
            async (order, sp, guid) =>
            {
                // UC-12 A3: nobody decided in time; ops follow up with the customer.
                await sp.GetRequiredService<IncidentManager>().OpenOnceAsync(IncidentType.ItemDecisionTimeout, IncidentSeverity.Medium,
                    order.Id, orderId: order.Id, restaurantId: order.RestaurantId);
                order.ResolveItemDecision(ItemDecision.ContinueWithout, ActorType.System, null, now, guid.Create);
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

        await RaiseAlertsAsync(services, settings, now);
    }

    /* Alerts that do not change the order, one incident per order ever (OpenOnceAsync):
     *   4. BR-023: no restaurant answer after N minutes (before the auto-cancel) → RESTAURANT_NO_RESPONSE;
     *   5. BR-058 / REQ-OPS-03: past the promised time by more than the threshold → ORDER_LATE for ops,
     *      and the customer is told once (outbox, customer push). */
    private async Task RaiseAlertsAsync(IServiceProvider services, ISettingProvider settings, DateTime now)
    {
        var orders = services.GetRequiredService<IRepository<Order, Guid>>();
        var incidents = services.GetRequiredService<IRepository<Incident, Guid>>();
        var incidentManager = services.GetRequiredService<IncidentManager>();
        var outbox = services.GetRequiredService<OutboxManager>();
        var unitOfWorkManager = services.GetRequiredService<IUnitOfWorkManager>();
        var asyncExecuter = services.GetRequiredService<Volo.Abp.Linq.IAsyncQueryableExecuter>();

        var alertBefore = now.AddMinutes(-await settings.GetAsync<int>(SahamSettings.Orders.RestaurantAlertMinutes));
        var lateBefore = now.AddMinutes(-await settings.GetAsync<int>(SahamSettings.Orders.LateThresholdMinutes));

        try
        {
            using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
            var allIncidents = await incidents.GetQueryableAsync();

            var noResponse = await asyncExecuter.ToListAsync((await orders.GetQueryableAsync())
                .Where(o => o.Status == OrderStatus.Placed && o.PlacedAt <= alertBefore &&
                            !allIncidents.Any(i => i.OrderId == o.Id && i.IncidentType == IncidentType.RestaurantNoResponse))
                .OrderBy(o => o.PlacedAt)
                .Select(o => new { o.Id, o.RestaurantId })
                .Take(BatchSize));
            foreach (var o in noResponse)
            {
                await incidentManager.OpenOnceAsync(IncidentType.RestaurantNoResponse, IncidentSeverity.High, o.Id,
                    orderId: o.Id, restaurantId: o.RestaurantId);
            }

            var late = await asyncExecuter.ToListAsync((await orders.GetQueryableAsync())
                .Where(o => (o.Status == OrderStatus.Placed || o.Status == OrderStatus.Accepted ||
                             o.Status == OrderStatus.Ready || o.Status == OrderStatus.PickedUp) &&
                            o.EtaMaxAt <= lateBefore &&
                            !allIncidents.Any(i => i.OrderId == o.Id && i.IncidentType == IncidentType.OrderLate))
                .OrderBy(o => o.PlacedAt)
                .Select(o => new { o.Id, o.RestaurantId })
                .Take(BatchSize));
            foreach (var o in late)
            {
                if (await incidentManager.OpenOnceAsync(IncidentType.OrderLate, IncidentSeverity.Medium, o.Id,
                        orderId: o.Id, restaurantId: o.RestaurantId) != null)
                {
                    await outbox.EnqueueAsync(OutboxEventTypes.OrderLate, OutboxAggregateTypes.Order, o.Id,
                        new Dictionary<string, string?> { ["orderId"] = o.Id.ToString() });
                }
            }

            await uow.CompleteAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Order alerts failed");
        }
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
