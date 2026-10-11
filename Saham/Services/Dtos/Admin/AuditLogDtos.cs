using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Auditing;

namespace Saham.Services.Dtos.Admin;

/* UC-29 / REQ-OPS-11: one request in the audit log, with who, when, from where, what was called,
 * the reason typed for a ✱ action, and every entity it changed (before → after). */
public class AuditLogDto : EntityDto<Guid>
{
    public DateTime ExecutionTime { get; set; }

    public int ExecutionDuration { get; set; }

    public Guid? UserId { get; set; }

    public string? UserName { get; set; }

    public string? ActorRoles { get; set; }

    public string? HttpMethod { get; set; }

    public string? Url { get; set; }

    public int? HttpStatusCode { get; set; }

    public string? ClientIpAddress { get; set; }

    public string? BrowserInfo { get; set; }

    public string? CorrelationId { get; set; }

    public string? Reason { get; set; }

    public bool HasException { get; set; }

    public List<AuditLogActionDto> Actions { get; set; } = new();

    public List<EntityChangeDto> EntityChanges { get; set; } = new();
}

public class AuditLogActionDto
{
    public string? ServiceName { get; set; }

    public string? MethodName { get; set; }

    public DateTime ExecutionTime { get; set; }
}

public class EntityChangeDto : EntityDto<Guid>
{
    public DateTime ChangeTime { get; set; }

    public EntityChangeType ChangeType { get; set; }

    /* Short type name, e.g. "Order"; the full name is in EntityTypeFullName. */
    public string EntityType { get; set; } = string.Empty;

    public string? EntityTypeFullName { get; set; }

    public string? EntityId { get; set; }

    public List<EntityPropertyChangeDto> PropertyChanges { get; set; } = new();

    // Filled when the change is listed on its own (entity history)
    public Guid? AuditLogId { get; set; }

    public Guid? UserId { get; set; }

    public string? UserName { get; set; }

    public string? ActorRoles { get; set; }

    public string? Reason { get; set; }

    public string? Operation { get; set; }
}

public class EntityPropertyChangeDto
{
    public string PropertyName { get; set; } = string.Empty;

    /* JSON text of the value, as recorded by ABP (strings are quoted). */
    public string? OriginalValue { get; set; }

    public string? NewValue { get; set; }
}

/* Search by user, entity, period and kind of action (UC-29). */
public class GetAuditLogsInput : PagedResultRequestDto
{
    public DateTime? StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public Guid? UserId { get; set; }

    public string? UserName { get; set; }

    /* Short ("Order") or full type name. */
    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    /* Part of the service or method name, e.g. "Cancel" or "OpsOrder". */
    public string? Operation { get; set; }

    public string? HttpMethod { get; set; }

    public bool? HasException { get; set; }

    /* Only requests that changed something (the default: reads are noise here). */
    public bool ChangesOnly { get; set; } = true;
}

public class GetEntityHistoryInput
{
    [Required]
    public string EntityType { get; set; } = string.Empty;

    [Required]
    public string EntityId { get; set; } = string.Empty;
}
