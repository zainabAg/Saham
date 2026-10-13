using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Saham.Data;
using Saham.Entities.Notifications;
using Saham.Infrastructure.Outbox;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Saham.BackgroundWorkers;

/* The outbox relay and the notification queue (SRS I-6, Phase 6) — the ONLY sender of outbox
 * notifications; safe with several backend instances:
 *   1. relay: each committed, unpublished event is locked (FOR UPDATE SKIP LOCKED), turned into its
 *      notifications (one per channel + recipient, unique) and stamped published — one short
 *      transaction, no provider call inside it;
 *   2. delivery: NotificationQueue → OutboxProcessor claims one notification at a time under a lease,
 *      calls the provider outside any transaction, and records the outcome only while it owns the lease.
 * A database error ends the cycle; the next tick starts again. On shutdown an in-flight send is
 * abandoned without recording success; its lease expires and another worker takes it back. */
public class NotificationDispatchWorker : AsyncPeriodicBackgroundWorkerBase
{
    private const int OutboxBatchSize = 100;
    private const int NotificationBatchSize = 50;

    public NotificationDispatchWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromSeconds(5).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var services = workerContext.ServiceProvider;
        var health = services.GetRequiredService<NotificationWorkerHealth>();
        var clock = services.GetRequiredService<IClock>();
        try
        {
            await PublishPendingAsync(services, workerContext.CancellationToken);
            var result = await services.GetRequiredService<NotificationQueue>()
                .RunOnceAsync(NotificationBatchSize, workerContext.CancellationToken);
            health.Succeeded(clock.Now, result.LostLeases);
            if (result.LostLeases > 0)
            {
                Logger.LogWarning("Notification queue: {Lost} lease(s) lost before the outcome was recorded", result.LostLeases);
            }
        }
        catch (OperationCanceledException) when (workerContext.CancellationToken.IsCancellationRequested)
        {
            // Shutting down: nothing was recorded as sent; leases expire and are claimed again.
        }
        catch (Exception ex)
        {
            health.Failed(clock.Now, ex);
            // The exception type only: provider/DB messages may carry phone numbers, tokens or codes.
            Logger.LogWarning("Notification worker cycle failed ({ErrorType}); retrying on the next tick", ex.GetType().Name);
        }
    }

    private async Task PublishPendingAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var unitOfWorkManager = services.GetRequiredService<IUnitOfWorkManager>();
        var repository = services.GetRequiredService<IRepository<OutboxEvent, long>>();
        var planner = services.GetRequiredService<NotificationPlanner>();
        var clock = services.GetRequiredService<IClock>();
        var contextProvider = services.GetRequiredService<IDbContextProvider<SahamDbContext>>();

        for (var i = 0; i < OutboxBatchSize; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long? failedId = null;
            Exception? failure = null;
            using (var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
            {
                // Locked until this transaction ends; another instance skips it and takes the next one.
                var db = await contextProvider.GetDbContextAsync();
                var outboxEvent = (await db.Set<OutboxEvent>()
                        .FromSqlRaw("""SELECT * FROM "AppOutboxEvents" WHERE "PublishedAt" IS NULL ORDER BY "Id" LIMIT 1 FOR UPDATE SKIP LOCKED""")
                        .ToListAsync(cancellationToken))
                    .FirstOrDefault();
                if (outboxEvent == null)
                {
                    return;
                }

                try
                {
                    await planner.PlanAsync(outboxEvent);
                    outboxEvent.MarkPublished(clock.Now);
                    await repository.UpdateAsync(outboxEvent);
                    await uow.CompleteAsync(cancellationToken);
                    continue;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failedId = outboxEvent.Id;
                    failure = ex;
                }
            }

            Logger.LogWarning("Outbox event {OutboxEventId} could not be published ({ErrorType})", failedId, failure!.GetType().Name);
            await RecordPublishFailureAsync(unitOfWorkManager, repository, clock, failedId!.Value, failure);
        }
    }

    private static async Task RecordPublishFailureAsync(IUnitOfWorkManager unitOfWorkManager,
        IRepository<OutboxEvent, long> repository, IClock clock, long id, Exception error)
    {
        try
        {
            using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
            var outboxEvent = await repository.GetAsync(id);
            outboxEvent.MarkFailed(error.GetType().Name, clock.Now); // a safe code, not the message
            await repository.UpdateAsync(outboxEvent);
            await uow.CompleteAsync();
        }
        catch
        {
            // Retried on the next sweep.
        }
    }
}
