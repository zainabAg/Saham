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

/* Open wallet payments (UC-05):
 *   A1 — no webhook for a minute: ask the provider (each payment at most once a minute);
 *   E2 — past the time limit (15 min): one last question, then the payment expires and the order is
 *        cancelled with PAYMENT_TIMEOUT (and its coupon released).
 * Each payment in its own transaction; a concurrent webhook wins (concurrency stamp). */
public class PaymentTimeoutWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const int BatchSize = 50;

    public PaymentTimeoutWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromSeconds(20).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var services = workerContext.ServiceProvider;
        var repository = services.GetRequiredService<IRepository<Payment, Guid>>();
        var unitOfWorkManager = services.GetRequiredService<IUnitOfWorkManager>();
        var asyncExecuter = services.GetRequiredService<IAsyncQueryableExecuter>();
        var manager = services.GetRequiredService<PaymentManager>();
        var now = services.GetRequiredService<IClock>().Now;
        var checkBefore = now.AddMinutes(-1);

        List<(Guid Id, bool Expired)> due;
        using (var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            due = (await asyncExecuter.ToListAsync((await repository.GetQueryableAsync())
                    .Where(p => (p.Status == PaymentStatus.Initiated || p.Status == PaymentStatus.Pending) &&
                                (p.ExpiresAt <= now || p.CreationTime <= checkBefore && (p.LastCheckedAt == null || p.LastCheckedAt <= checkBefore)))
                    .OrderBy(p => p.ExpiresAt)
                    .Select(p => new { p.Id, Expired = p.ExpiresAt <= now })
                    .Take(BatchSize)))
                .Select(x => (x.Id, x.Expired)).ToList();
            await uow.CompleteAsync();
        }

        foreach (var (id, expired) in due)
        {
            try
            {
                using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
                var payment = await repository.GetAsync(id);
                if (expired)
                {
                    await manager.ExpireOrCaptureAsync(payment);
                }
                else
                {
                    await manager.PollAsync(payment);
                }

                await uow.CompleteAsync();
            }
            catch (AbpDbConcurrencyException)
            {
                // A webhook settled it at the same moment.
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Payment check failed for payment {PaymentId}", id);
            }
        }
    }
}
