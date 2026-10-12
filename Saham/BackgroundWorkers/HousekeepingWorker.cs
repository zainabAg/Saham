using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Saham.Entities.Identity;
using Saham.Infrastructure.Idempotency;
using Saham.Settings;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Settings;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Saham.BackgroundWorkers;

/* Periodic deletion of short-lived technical rows (SRS 13.7):
 * idempotency keys after the retention period, and used/expired OTP codes. */
public class HousekeepingWorker : AsyncPeriodicBackgroundWorkerBase
{
    public HousekeepingWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromHours(1).TotalMilliseconds;
        Timer.RunOnStart = true; // also right after a (re)start, not only an hour later
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var services = workerContext.ServiceProvider;
        using var uow = services.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
        var clock = services.GetRequiredService<IClock>();
        var settings = services.GetRequiredService<ISettingProvider>();

        // EX-11: kept 7 days. After deletion a reused key runs again: the lasting guards are the business ones
        // (one order per quote, state transitions, unique ledger keys), not this table.
        var retentionDays = await settings.GetAsync<int>(SahamSettings.System.IdempotencyRetentionDays);
        var idempotencyCutoff = clock.Now.AddDays(-retentionDays);
        await services.GetRequiredService<IRepository<IdempotencyRecord, Guid>>()
            .DeleteDirectAsync(x => x.CreationTime < idempotencyCutoff);

        var otpCutoff = clock.Now.AddDays(-1);
        await services.GetRequiredService<IRepository<OtpChallenge, Guid>>()
            .DeleteDirectAsync(x => x.ExpiresAt < otpCutoff);

        await uow.CompleteAsync();
    }
}
