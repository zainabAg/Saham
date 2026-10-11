using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Saham.Entities.Support;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace Saham.BackgroundWorkers;

/* In-app calls nobody picked up within the ring time become NO_ANSWER — a valid unanswered attempt
 * for "not received" (BR-063) — and both apps stop ringing. */
public class CallTimeoutWorker : AsyncPeriodicBackgroundWorkerBase
{
    public CallTimeoutWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromSeconds(10).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        try
        {
            using var uow = workerContext.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
            await workerContext.ServiceProvider.GetRequiredService<VoiceCallManager>().ExpireRingingAsync();
            await uow.CompleteAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Call timeout sweep failed");
        }
    }
}
