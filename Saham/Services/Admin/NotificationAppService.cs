using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Notifications;
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
}

/* The notification queue for ops and the owner (SRS I-6): by default the messages that failed
 * or were given up — e.g. a delivery-code SMS that never went out (REQ-NTF-02). A given-up message
 * can be sent again by hand. The text itself is never shown here (it may contain a code). */
[Authorize(SahamPermissions.Notifications.View)]
public class NotificationAppService : SahamAppService, INotificationAppService
{
    private readonly IRepository<Notification, Guid> _repository;

    public NotificationAppService(IRepository<Notification, Guid> repository)
    {
        _repository = repository;
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

    [Authorize(SahamPermissions.Notifications.Retry)]
    public async Task<NotificationDto> RetryAsync(Guid id)
    {
        var notification = await _repository.GetAsync(id);
        notification.Requeue(Clock.Now);

        await _repository.UpdateAsync(notification);
        return ObjectMapper.Map<Notification, NotificationDto>(notification);
    }
}
