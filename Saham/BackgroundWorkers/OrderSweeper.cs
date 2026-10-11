using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Saham.Entities.Orders;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Uow;

namespace Saham.BackgroundWorkers;

/* The order timers' common loop: pick the matching order ids, then handle each order in its own
 * transaction, re-checking the condition on the fresh row; a concurrent change wins. */
public static class OrderSweeper
{
    public static async Task ProcessAsync(
        IServiceProvider services,
        Func<IQueryable<Order>, IQueryable<Order>> filter,
        Func<Order, IServiceProvider, IGuidGenerator, Task> action,
        ILogger logger,
        int batchSize)
    {
        var repository = services.GetRequiredService<IRepository<Order, Guid>>();
        var unitOfWorkManager = services.GetRequiredService<IUnitOfWorkManager>();
        var guidGenerator = services.GetRequiredService<IGuidGenerator>();
        var asyncExecuter = services.GetRequiredService<Volo.Abp.Linq.IAsyncQueryableExecuter>();

        List<Guid> ids;
        using (var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            ids = await asyncExecuter.ToListAsync(filter(await repository.GetQueryableAsync())
                .OrderBy(o => o.CreationTime).Select(o => o.Id).Take(batchSize));
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
                logger.LogWarning(ex, "Order timer failed for order {OrderId}", id);
            }
        }
    }
}
