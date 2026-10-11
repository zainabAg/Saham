using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Saham.Entities.Notifications;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Saham.BackgroundWorkers;

/* The outbox relay and the notification queue (SRS I-6), swept from the database so nothing is
 * lost on restart:
 *   1. OutboxRelay.publishPending: each unpublished event becomes its notifications and is stamped
 *      published, in one transaction (NotificationPlanner);
 *   2. NotificationService.processDue: each due notification is sent, retried with growing gaps,
 *      then given up (NotificationSender).
 * Every item runs in its own transaction: one bad item never blocks the others. */
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
        await PublishPendingAsync(workerContext.ServiceProvider);
        await SendDueAsync(workerContext.ServiceProvider);
    }

    private async Task PublishPendingAsync(IServiceProvider services)
    {
        var unitOfWorkManager = services.GetRequiredService<IUnitOfWorkManager>();
        var repository = services.GetRequiredService<IRepository<OutboxEvent, long>>();
        var planner = services.GetRequiredService<NotificationPlanner>();
        var clock = services.GetRequiredService<IClock>();
        var asyncExecuter = services.GetRequiredService<IAsyncQueryableExecuter>();

        List<long> ids;
        using (var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            ids = await asyncExecuter.ToListAsync((await repository.GetQueryableAsync())
                .Where(e => e.PublishedAt == null)
                .OrderBy(e => e.Id)
                .Select(e => e.Id)
                .Take(OutboxBatchSize));
            await uow.CompleteAsync();
        }

        foreach (var id in ids)
        {
            try
            {
                using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
                var outboxEvent = await repository.GetAsync(id);
                if (outboxEvent.PublishedAt != null)
                {
                    continue;
                }

                await planner.PlanAsync(outboxEvent);
                outboxEvent.MarkPublished(clock.Now);
                await repository.UpdateAsync(outboxEvent);
                await uow.CompleteAsync();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Outbox event {OutboxEventId} could not be published", id);
                await RecordPublishFailureAsync(unitOfWorkManager, repository, clock, id, ex);
            }
        }
    }

    private static async Task RecordPublishFailureAsync(IUnitOfWorkManager unitOfWorkManager,
        IRepository<OutboxEvent, long> repository, IClock clock, long id, Exception error)
    {
        try
        {
            using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
            var outboxEvent = await repository.GetAsync(id);
            outboxEvent.MarkFailed(error.Message, clock.Now);
            await repository.UpdateAsync(outboxEvent);
            await uow.CompleteAsync();
        }
        catch
        {
            // Retried on the next sweep.
        }
    }

    private async Task SendDueAsync(IServiceProvider services)
    {
        var unitOfWorkManager = services.GetRequiredService<IUnitOfWorkManager>();
        var repository = services.GetRequiredService<IRepository<Notification, Guid>>();
        var sender = services.GetRequiredService<NotificationSender>();
        var asyncExecuter = services.GetRequiredService<IAsyncQueryableExecuter>();
        var now = services.GetRequiredService<IClock>().Now;

        List<Guid> ids;
        using (var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            ids = await asyncExecuter.ToListAsync((await repository.GetQueryableAsync())
                .Where(n => (n.Status == NotificationStatus.Queued || n.Status == NotificationStatus.Failed) && n.NextAttemptAt <= now)
                .OrderByDescending(n => n.HighPriority)
                .ThenBy(n => n.NextAttemptAt)
                .Select(n => n.Id)
                .Take(NotificationBatchSize));
            await uow.CompleteAsync();
        }

        foreach (var id in ids)
        {
            try
            {
                using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
                var notification = await repository.GetAsync(id);
                await sender.SendAsync(notification);
                await repository.UpdateAsync(notification);
                await uow.CompleteAsync();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Notification {NotificationId} could not be processed", id);
            }
        }
    }
}
