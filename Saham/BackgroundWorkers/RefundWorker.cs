using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Saham.Entities.Payments;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Saham.BackgroundWorkers;

/* UC-22: sends the due refunds to the wallet provider, one transaction each, outside the request
 * that created them (BR-041). Success → J-REFUND-SETTLED + REFUND_SUCCEEDED to the customer;
 * a retryable failure waits (1, 5, 15, 60, 180 min); then MANUAL_REVIEW + a REFUND_FAILED incident. */
public class RefundWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const int BatchSize = 20;

    public RefundWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromSeconds(10).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var services = workerContext.ServiceProvider;
        var repository = services.GetRequiredService<IRepository<Refund, Guid>>();
        var unitOfWorkManager = services.GetRequiredService<IUnitOfWorkManager>();
        var asyncExecuter = services.GetRequiredService<IAsyncQueryableExecuter>();
        var manager = services.GetRequiredService<PaymentManager>();
        var now = services.GetRequiredService<IClock>().Now;

        List<Guid> ids;
        using (var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            ids = await asyncExecuter.ToListAsync((await repository.GetQueryableAsync())
                .Where(r => (r.Status == RefundStatus.Pending || r.Status == RefundStatus.FailedRetryable) && r.NextAttemptAt <= now)
                .OrderBy(r => r.NextAttemptAt)
                .Select(r => r.Id)
                .Take(BatchSize));
            await uow.CompleteAsync();
        }

        foreach (var id in ids)
        {
            try
            {
                using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
                var refund = await repository.GetAsync(id);
                await manager.ProcessRefundAsync(refund);
                await repository.UpdateAsync(refund);
                await uow.CompleteAsync();
            }
            catch (AbpDbConcurrencyException)
            {
                // Someone else (the accountant) changed it first.
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Refund {RefundId} could not be processed", id);
            }
        }
    }
}
