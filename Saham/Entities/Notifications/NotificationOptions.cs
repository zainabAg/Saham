using System.Collections.Generic;

namespace Saham.Entities.Notifications;

/* Configuration section "Notifications". SimulateFailure (e.g. ["Sms"]) makes every send on that
 * channel fail, to exercise retries and the final-failure incident in a test run. Never set in production. */
public class NotificationOptions
{
    public List<NotificationChannel> SimulateFailure { get; set; } = new();
}
