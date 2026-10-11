using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Infrastructure.Auditing;
using Saham.Permissions;
using Saham.Services.Dtos.Admin;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.AuditLogging;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;

namespace Saham.Services.Admin;

public interface IAuditLogAppService : IApplicationService
{
    Task<PagedResultDto<AuditLogDto>> GetListAsync(GetAuditLogsInput input);

    Task<ListResultDto<EntityChangeDto>> GetEntityHistoryAsync(GetEntityHistoryInput input);
}

/* The audit log for the owner (REQ-OPS-11, UC-29, BR-055), read from ABP's audit tables:
 * AbpAuditLogs (who, when, IP, browser, request id, the ✱ reason and roles in ExtraProperties)
 * and AbpEntityChanges / AbpEntityPropertyChanges (before → after for every Saham entity, users,
 * roles, permissions and settings — SahamModule.ConfigureEntityHistory). Read only: nothing here
 * can change or delete a log. */
[Authorize(SahamPermissions.Audit.View)]
public class AuditLogAppService : SahamAppService, IAuditLogAppService
{
    private readonly IRepository<AuditLog, Guid> _auditLogRepository;

    public AuditLogAppService(IRepository<AuditLog, Guid> auditLogRepository)
    {
        _auditLogRepository = auditLogRepository;
    }

    public async Task<PagedResultDto<AuditLogDto>> GetListAsync(GetAuditLogsInput input)
    {
        var entityType = NormalizeType(input.EntityType);
        var queryable = (await _auditLogRepository.WithDetailsAsync())
            .WhereIf(input.StartTime.HasValue, a => a.ExecutionTime >= input.StartTime)
            .WhereIf(input.EndTime.HasValue, a => a.ExecutionTime <= input.EndTime)
            .WhereIf(input.UserId.HasValue, a => a.UserId == input.UserId)
            .WhereIf(!input.UserName.IsNullOrWhiteSpace(), a => a.UserName != null && a.UserName.Contains(input.UserName!))
            .WhereIf(!input.HttpMethod.IsNullOrWhiteSpace(), a => a.HttpMethod == input.HttpMethod!.ToUpper())
            .WhereIf(input.HasException == true, a => a.Exceptions != null && a.Exceptions != "")
            .WhereIf(input.HasException == false, a => a.Exceptions == null || a.Exceptions == "")
            .WhereIf(!input.Operation.IsNullOrWhiteSpace(), a => a.Actions.Any(x =>
                (x.ServiceName != null && x.ServiceName.Contains(input.Operation!)) ||
                (x.MethodName != null && x.MethodName.Contains(input.Operation!))))
            .WhereIf(entityType != null, a => a.EntityChanges.Any(c =>
                c.EntityTypeFullName == entityType || c.EntityTypeFullName.EndsWith("." + entityType)))
            .WhereIf(!input.EntityId.IsNullOrWhiteSpace(), a => a.EntityChanges.Any(c => c.EntityId == input.EntityId))
            .WhereIf(input.ChangesOnly, a => a.EntityChanges.Any());

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var logs = await AsyncExecuter.ToListAsync(queryable
            .OrderByDescending(a => a.ExecutionTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<AuditLogDto>(totalCount, logs.Select(MapLog).ToList());
    }

    /* "What happened to this order / driver / setting", oldest first, with who did it and why. */
    public async Task<ListResultDto<EntityChangeDto>> GetEntityHistoryAsync(GetEntityHistoryInput input)
    {
        var entityType = NormalizeType(input.EntityType)!;
        var logs = await AsyncExecuter.ToListAsync((await _auditLogRepository.WithDetailsAsync())
            .Where(a => a.EntityChanges.Any(c => c.EntityId == input.EntityId &&
                (c.EntityTypeFullName == entityType || c.EntityTypeFullName.EndsWith("." + entityType))))
            .OrderBy(a => a.ExecutionTime)
            .Take(500));

        var changes = logs.SelectMany(log => log.EntityChanges
                .Where(c => c.EntityId == input.EntityId && Matches(c.EntityTypeFullName, entityType))
                .Select(c =>
                {
                    var dto = MapChange(c);
                    dto.AuditLogId = log.Id;
                    dto.UserId = log.UserId;
                    dto.UserName = log.UserName;
                    dto.ActorRoles = log.GetProperty<string>(SahamAuditLogContributor.ActorRolesProperty);
                    dto.Reason = log.GetProperty<string>(SahamAuditLogContributor.ReasonProperty);
                    dto.Operation = log.Actions.Select(x => $"{ShortName(x.ServiceName)}.{x.MethodName}").FirstOrDefault();
                    return dto;
                }))
            .OrderBy(c => c.ChangeTime)
            .ToList();

        return new ListResultDto<EntityChangeDto>(changes);
    }

    private static AuditLogDto MapLog(AuditLog log) => new()
    {
        Id = log.Id,
        ExecutionTime = log.ExecutionTime,
        ExecutionDuration = log.ExecutionDuration,
        UserId = log.UserId,
        UserName = log.UserName,
        ActorRoles = log.GetProperty<string>(SahamAuditLogContributor.ActorRolesProperty),
        HttpMethod = log.HttpMethod,
        Url = log.Url,
        HttpStatusCode = log.HttpStatusCode,
        ClientIpAddress = log.ClientIpAddress,
        BrowserInfo = log.BrowserInfo,
        CorrelationId = log.CorrelationId,
        Reason = log.GetProperty<string>(SahamAuditLogContributor.ReasonProperty),
        HasException = !log.Exceptions.IsNullOrWhiteSpace(),
        Actions = log.Actions.OrderBy(x => x.ExecutionTime).Select(x => new AuditLogActionDto
        {
            ServiceName = ShortName(x.ServiceName),
            MethodName = x.MethodName,
            ExecutionTime = x.ExecutionTime
        }).ToList(),
        EntityChanges = log.EntityChanges.OrderBy(c => c.ChangeTime).Select(MapChange).ToList()
    };

    private static EntityChangeDto MapChange(EntityChange change) => new()
    {
        Id = change.Id,
        ChangeTime = change.ChangeTime,
        ChangeType = change.ChangeType,
        EntityType = ShortName(change.EntityTypeFullName) ?? string.Empty,
        EntityTypeFullName = change.EntityTypeFullName,
        EntityId = change.EntityId,
        PropertyChanges = change.PropertyChanges.Select(p => new EntityPropertyChangeDto
        {
            PropertyName = p.PropertyName,
            OriginalValue = Readable(p.PropertyTypeFullName, p.OriginalValue),
            NewValue = Readable(p.PropertyTypeFullName, p.NewValue)
        }).ToList()
    };

    /* ABP records enums as numbers ("2" → "7"); the owner reads names ("Placed" → "Cancelled"). */
    private static string? Readable(string? typeFullName, string? value)
    {
        if (value.IsNullOrWhiteSpace() || typeFullName.IsNullOrWhiteSpace() || !int.TryParse(value, out var number))
        {
            return value;
        }

        var type = typeof(SahamModule).Assembly.GetType(UnwrapNullable(typeFullName!));
        return type is { IsEnum: true } && Enum.IsDefined(type, number) ? Enum.GetName(type, number) : value;
    }

    /* "System.Nullable`1[[Saham.Entities.Orders.OrderHoldReason, Saham, ...]]" → "Saham.Entities.Orders.OrderHoldReason" */
    private static string UnwrapNullable(string typeFullName)
    {
        const string prefix = "System.Nullable`1[[";
        return typeFullName.StartsWith(prefix) ? typeFullName[prefix.Length..].Split(',')[0] : typeFullName;
    }

    private static string? NormalizeType(string? type) => type.IsNullOrWhiteSpace() ? null : type!.Trim();

    private static bool Matches(string fullName, string type) => fullName == type || fullName.EndsWith("." + type);

    private static string? ShortName(string? fullName) => fullName?[(fullName.LastIndexOf('.') + 1)..];
}
