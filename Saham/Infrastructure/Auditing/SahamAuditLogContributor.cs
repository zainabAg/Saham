using System;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Auditing;
using Volo.Abp.Users;

namespace Saham.Infrastructure.Auditing;

/* Adds to every audit log what the SRS audit_logs table has and ABP's does not (BR-055):
 *   ActorRoles — the role(s) of the user at the time of the action (actor_role);
 *   Reason     — the reason typed for an action marked ✱ (cancel, "without code", "not received",
 *                manual assignment, suspension, pause by ops...). It is taken from the request's own
 *                Reason / Note / Resolution field, so no service can forget to record it.
 * Both go to AbpAuditLogs.ExtraProperties and are shown by AuditLogAppService. */
public class SahamAuditLogContributor : AuditLogContributor
{
    public const string ReasonProperty = "Reason";
    public const string ActorRolesProperty = "ActorRoles";

    private static readonly string[] ReasonFields = { "reason", "note", "resolution" };

    public override void PostContribute(AuditLogContributionContext context)
    {
        var log = context.AuditInfo;

        var currentUser = context.ServiceProvider.GetService<ICurrentUser>();
        if (currentUser is { IsAuthenticated: true } && currentUser.Roles.Length > 0)
        {
            log.ExtraProperties[ActorRolesProperty] = string.Join(",", currentUser.Roles.OrderBy(r => r));
        }

        foreach (var action in log.Actions)
        {
            var reason = FindReason(action.Parameters);
            if (!reason.IsNullOrWhiteSpace())
            {
                log.ExtraProperties[ReasonProperty] = reason;
                break;
            }
        }
    }

    private static string? FindReason(string? parametersJson)
    {
        if (parametersJson.IsNullOrWhiteSpace())
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(parametersJson!);
            return FindReason(document.RootElement, depth: 0);
        }
        catch (JsonException)
        {
            return null; // ABP truncates very long parameter lists; nothing to read then.
        }
    }

    private static string? FindReason(JsonElement element, int depth)
    {
        if (element.ValueKind != JsonValueKind.Object || depth > 3)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String &&
                ReasonFields.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
            {
                var value = property.Value.GetString();
                if (!value.IsNullOrWhiteSpace())
                {
                    return value;
                }
            }
        }

        foreach (var property in element.EnumerateObject())
        {
            var nested = FindReason(property.Value, depth + 1);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }
}
