using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Support;

public enum IncidentType
{
    NoDriver = 0,
    DriverStale = 1,
    RestaurantNoResponse = 2,
    OrderLate = 3,
    ItemDecisionTimeout = 4,
    RestaurantCancelledAccepted = 5,
    DeliveryCodeLocked = 6,
    CustomerNotAnswering = 7,
    DeliveredFarFromAddress = 8
}

public enum IncidentSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public enum IncidentStatus
{
    Open = 0,
    Acknowledged = 1,
    Resolved = 2
}

/* Something ops must look at (SRS table incidents). The dedupe key (e.g. "NO_DRIVER:{order}")
 * has a partial unique index while not resolved, so the same problem is never opened twice. */
public class Incident : CreationAuditedAggregateRoot<Guid>
{
    public IncidentType IncidentType { get; private set; }

    public IncidentSeverity Severity { get; private set; }

    public Guid? OrderId { get; private set; }

    public Guid? DriverId { get; private set; }

    public Guid? RestaurantId { get; private set; }

    public IncidentStatus Status { get; private set; }

    public Guid? AssignedTo { get; private set; }

    public DateTime OpenedAt { get; private set; }

    public DateTime? ResolvedAt { get; private set; }

    public string? Resolution { get; private set; }

    public string DedupeKey { get; private set; }

    protected Incident()
    {
        DedupeKey = null!;
    }

    internal Incident(Guid id, IncidentType type, IncidentSeverity severity, string dedupeKey,
        Guid? orderId, Guid? driverId, Guid? restaurantId, DateTime now)
        : base(id)
    {
        IncidentType = type;
        Severity = severity;
        DedupeKey = dedupeKey;
        OrderId = orderId;
        DriverId = driverId;
        RestaurantId = restaurantId;
        Status = IncidentStatus.Open;
        OpenedAt = now;
    }

    public void Acknowledge(Guid userId)
    {
        if (Status == IncidentStatus.Open)
        {
            Status = IncidentStatus.Acknowledged;
            AssignedTo = userId;
        }
    }

    public void Resolve(string resolution, DateTime now)
    {
        Status = IncidentStatus.Resolved;
        Resolution = resolution;
        ResolvedAt = now;
    }
}
