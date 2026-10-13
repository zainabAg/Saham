using System;
using System.Threading.Tasks;

namespace Saham.Integrations.Push;

/* Push to a user's devices (SRS PushProvider). FcmPushProvider replaces the logging adapter once
 * Firebase keys and device registration exist. Returns the provider's message id. */
public interface IPushSender
{
    Task<string?> SendAsync(PushMessage message);
}

/* CollapseKey (Phase 6): the notification id; a resend after a crash replaces the shown notification
 * instead of adding a second one (FCM has no idempotency key, so this is best effort). */
public record PushMessage(Guid UserId, string Title, string Body, bool HighPriority, Guid? OrderId, string? CollapseKey = null);
