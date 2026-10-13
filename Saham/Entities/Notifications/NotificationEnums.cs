namespace Saham.Entities.Notifications;

/* Realtime (SignalR) is sent directly after commit; the outbox carries the channels that must
 * survive a closed app or a provider failure. */
public enum NotificationChannel
{
    Sms = 0,
    Push = 1
}

/* SRS notifications.status: Failed = waiting for the next attempt, Dead = given up,
 * Processing = claimed by one worker under a lease (Phase 6). Sent = accepted by the provider,
 * not proof that the phone received it. */
public enum NotificationStatus
{
    Queued = 0,
    Sent = 1,
    Failed = 2,
    Dead = 3,
    Processing = 4
}
