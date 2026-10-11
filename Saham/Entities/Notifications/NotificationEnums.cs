namespace Saham.Entities.Notifications;

/* Realtime (SignalR) is sent directly after commit; the outbox carries the channels that must
 * survive a closed app or a provider failure. */
public enum NotificationChannel
{
    Sms = 0,
    Push = 1
}

/* SRS notifications.status: Failed = waiting for the next attempt, Dead = given up. */
public enum NotificationStatus
{
    Queued = 0,
    Sent = 1,
    Failed = 2,
    Dead = 3
}
