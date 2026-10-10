using System;
using NetTopologySuite.Geometries;
using Volo.Abp.Domain.Entities;

namespace Saham.Entities.Drivers;

/* Raw GPS trail (SRS table driver_locations): every point sent by the driver app, including
 * points queued while offline. Kept for a limited time (REC-19); the latest point is also
 * copied to Driver.LastLocation for dispatch. */
public class DriverLocation : BasicAggregateRoot<long>
{
    public Guid DriverId { get; private set; }

    public Guid? OrderId { get; private set; }

    public Point Location { get; private set; }

    public double? AccuracyM { get; private set; }

    /* Device time of the reading. */
    public DateTime RecordedAt { get; private set; }

    /* Server time of arrival. */
    public DateTime ReceivedAt { get; private set; }

    protected DriverLocation()
    {
        Location = null!;
    }

    public DriverLocation(Guid driverId, Guid? orderId, Point location, double? accuracyM, DateTime recordedAt, DateTime receivedAt)
    {
        DriverId = driverId;
        OrderId = orderId;
        Location = location;
        AccuracyM = accuracyM;
        RecordedAt = recordedAt;
        ReceivedAt = receivedAt;
    }
}
