using System;
using Saham.Entities.Notifications;
using Volo.Abp.Application.Dtos;

namespace Saham.Services.Dtos.Admin;

public class SmsLogDto : CreationAuditedEntityDto<Guid>
{
    public string PhoneNumber { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public SmsLogStatus Status { get; set; }

    public string? Provider { get; set; }

    public string? LastError { get; set; }
}

public class GetSmsLogsInput : PagedAndSortedResultRequestDto
{
    public string? PhoneNumber { get; set; }
}
