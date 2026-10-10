using System;

namespace Saham.Entities.Dispatch;

public enum DriverNotificationKind
{
    OfferCreated = 0,
    OfferCancelled = 1,
    TaskAssigned = 2,
    TaskReleased = 3
}

/* Local event for the driver's app (sent over SignalR after commit by DriverRealtimeNotifier). */
public record DriverNotificationEto(Guid DriverId, DriverNotificationKind Kind, Guid OrderId, Guid? OfferId, DateTime? ExpiresAt);
