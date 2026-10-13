using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Notifications;
using Saham.Infrastructure.Outbox;
using Saham.Permissions;
using Saham.Services.Dtos.Ops;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace Saham.Services.Admin;

public interface INotificationAppService : IApplicationService
{
    Task<PagedResultDto<NotificationDto>> GetListAsync(GetNotificationsInput input);

    Task<NotificationDto> RetryAsync(Guid id);

    Task<NotificationQueueHealthDto> GetHealthAsync();
}

/* The notification queue for ops and the owner (SRS I-6): by default the messages that failed
 * or were given up — e.g. a delivery-code SMS that never went out (REQ-NTF-02). A given-up message
 * can be sent again by hand. The text itself is never shown here (it may contain a code). */
[Authorize(SahamPermissions.Notifications.View)]
public class NotificationAppService : SahamAppService, INotificationAppService
{
    private readonly IRepository<Notification, Guid> _repository;
    private readonly IRepository<OutboxEvent, long> _outboxRepository;
    private readonly NotificationWorkerHealth _workerHealth;

    public NotificationAppService(
        IRepository<Notification, Guid> repository,
        IRepository<OutboxEvent, long> outboxRepository,
        NotificationWorkerHealth workerHealth)
    {
        _repository = repository;
        _outboxRepository = outboxRepository;
        _workerHealth = workerHealth;
    }

    public async Task<PagedResultDto<NotificationDto>> GetListAsync(GetNotificationsInput input)
    {
        var queryable = (await _repository.GetQueryableAsync())
            .WhereIf(input.FailedOnly && !input.Status.HasValue,
                n => n.Status == NotificationStatus.Failed || n.Status == NotificationStatus.Dead)
            .WhereIf(input.Status.HasValue, n => n.Status == input.Status)
            .WhereIf(input.Channel.HasValue, n => n.Channel == input.Channel)
            .WhereIf(input.OrderId.HasValue, n => n.OrderId == input.OrderId)
            .WhereIf(input.UserId.HasValue, n => n.UserId == input.UserId);

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var items = await AsyncExecuter.ToListAsync(queryable
            .OrderByDescending(n => n.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<NotificationDto>(totalCount, ObjectMapper.Map<List<Notification>, List<NotificationDto>>(items));
    }

    /* Phase 6: a dead message goes back to the queue with the SAME row, EventKey and id (the provider
     * idempotency key); Sent messages are refused. Permission-checked and recorded in the audit log. */
    [Authorize(SahamPermissions.Notifications.Retry)]
    public async Task<NotificationDto> RetryAsync(Guid id)
    {
        var notification = await _repository.GetAsync(id);
        notification.Requeue(Clock.Now);

        await _repository.UpdateAsync(notification);
        return ObjectMapper.Map<Notification, NotificationDto>(notification);
    }

    /* Phase 6 monitoring: DEAD, the age of what is due, expired leases, the relay's tail, worker errors. */
    public async Task<NotificationQueueHealthDto> GetHealthAsync()
    {
        var now = Clock.Now;
        var notifications = await _repository.GetQueryableAsync();
        var events = await _outboxRepository.GetQueryableAsync();
        var byStatus = await AsyncExecuter.ToListAsync(notifications.GroupBy(n => n.Status).Select(g => new { g.Key, Count = g.Count() }));
        var due = notifications.Where(n => (n.Status == NotificationStatus.Queued || n.Status == NotificationStatus.Failed) && n.NextAttemptAt <= now);
        var oldestDue = await AsyncExecuter.MinAsync(due.Select(n => (DateTime?)n.NextAttemptAt));
        var unpublished = events.Where(e => e.PublishedAt == null);
        var oldestEvent = await AsyncExecuter.MinAsync(unpublished.Select(e => (DateTime?)e.CreationTime));

        return new NotificationQueueHealthDto
        {
            NotificationsByStatus = byStatus.ToDictionary(x => x.Key.ToString(), x => x.Count),
            DeadNotifications = byStatus.Where(x => x.Key == NotificationStatus.Dead).Sum(x => x.Count),
            DueNotifications = await AsyncExecuter.CountAsync(due),
            OldestDueAgeSeconds = oldestDue.HasValue ? (int)(now - oldestDue.Value).TotalSeconds : null,
            ExpiredLeases = await AsyncExecuter.CountAsync(notifications.Where(n => n.Status == NotificationStatus.Processing && n.LeaseUntil <= now)),
            UnpublishedEvents = await AsyncExecuter.CountAsync(unpublished),
            OldestUnpublishedAgeSeconds = oldestEvent.HasValue ? (int)(now - oldestEvent.Value).TotalSeconds : null,
            FailedEvents = await AsyncExecuter.CountAsync(events.Where(e => e.LastError != null && e.Attempts >= OutboxEvent.MaxPublishAttempts)),
            WorkerLastRunAt = _workerHealth.LastRunAt,
            WorkerLastErrorAt = _workerHealth.LastErrorAt,
            WorkerLastErrorCode = _workerHealth.LastErrorCode,
            WorkerConsecutiveFailures = _workerHealth.ConsecutiveFailures,
            WorkerLostLeases = _workerHealth.LostLeases
        };
    }
}
