using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Saham.Entities.Dispatch;
using Saham.Entities.Orders;
using Saham.Settings;
using Volo.Abp;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Settings;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Saham.BackgroundWorkers;

/* The dispatch loop (UC-16), every few seconds, from the database so it survives restarts:
 *   1. pending offers older than 30 s → EXPIRED;
 *   2. every order that should have a driver and has no pending offer → next offer.
 * Each order runs in its own transaction. Two servers running the loop at the same time cannot
 * create two offers for one order: the "one pending offer per order" index rejects the second. */
public class DispatchWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const int BatchSize = 50;

    public DispatchWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromSeconds(3).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var services = workerContext.ServiceProvider;
        var unitOfWorkManager = services.GetRequiredService<IUnitOfWorkManager>();
        var dispatchManager = services.GetRequiredService<DispatchManager>();
        var orderRepository = services.GetRequiredService<IRepository<Order, Guid>>();
        var asyncExecuter = services.GetRequiredService<IAsyncQueryableExecuter>();
        var settings = services.GetRequiredService<ISettingProvider>();
        var now = services.GetRequiredService<IClock>().Now;

        using (var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            await dispatchManager.ExpireOffersAsync();
            await uow.CompleteAsync();
        }

        var startBefore = await settings.GetAsync<int>(SahamSettings.Dispatch.StartBeforeReadyMinutes);
        var readyBy = now.AddMinutes(startBefore);

        List<Guid> ids;
        using (var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            ids = await asyncExecuter.ToListAsync((await orderRepository.GetQueryableAsync())
                .Where(o => (o.Status == OrderStatus.Accepted || o.Status == OrderStatus.Ready)
                            && o.HoldReason == null
                            && (o.DispatchStatus == DispatchStatus.NotStarted || o.DispatchStatus == DispatchStatus.Searching)
                            && (o.Status == OrderStatus.Ready || o.EstimatedReadyAt <= readyBy))
                .OrderBy(o => o.AcceptedAt)
                .Select(o => o.Id)
                .Take(BatchSize));
            await uow.CompleteAsync();
        }

        foreach (var id in ids)
        {
            try
            {
                using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
                var order = await orderRepository.GetAsync(id);
                await dispatchManager.TryOfferNextAsync(order);
                await orderRepository.UpdateAsync(order);
                await uow.CompleteAsync();
            }
            catch (Exception ex) when (ex is AbpDbConcurrencyException or DbUpdateException or BusinessException)
            {
                // Another server or a user action got there first; the next tick sees the new state.
                Logger.LogDebug(ex, "Dispatch skipped order {OrderId}", id);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Dispatch failed for order {OrderId}", id);
            }
        }
    }
}
