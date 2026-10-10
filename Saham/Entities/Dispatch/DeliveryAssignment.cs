using System;
using NetTopologySuite.Geometries;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Dispatch;

/* A driver doing an order (SRS table delivery_assignments). An order can pass through several
 * drivers (release and reassign), but a partial unique index allows only one active
 * (Assigned / PickedUp) assignment per order — the final guarantee against double assignment
 * (EX-06, TC-14). Pickup, delivery and proof fields are filled in phase 6. */
public class DeliveryAssignment : CreationAuditedAggregateRoot<Guid>
{
    public Guid OrderId { get; private set; }

    public Guid DriverId { get; private set; }

    /* Empty for a manual assignment. */
    public Guid? OfferId { get; private set; }

    public AssignedByType AssignedByType { get; private set; }

    public Guid? AssignedByUser { get; private set; }

    public AssignmentStatus Status { get; private set; }

    /* Second order of a driver who already has one (fixed pay, BR-017 / ASM-32). */
    public bool IsBatch { get; private set; }

    public long DriverPayAmount { get; private set; }

    public DateTime AssignedAt { get; private set; }

    /* "Arrived at the restaurant" — basis of half pay on cancellation (ASM-37). */
    public DateTime? ArrivedAtRestaurantAt { get; private set; }

    public DateTime? PickedUpAt { get; private set; }

    public DateTime? ArrivedAtCustomerAt { get; private set; }

    public DateTime? DeliveredAt { get; private set; }

    public DateTime? ReleasedAt { get; private set; }

    public string? ReleaseReason { get; private set; }

    public Point? DeliveryLocation { get; private set; }

    public bool GeofenceFlag { get; private set; }

    protected DeliveryAssignment()
    {
    }

    internal DeliveryAssignment(
        Guid id,
        Guid orderId,
        Guid driverId,
        Guid? offerId,
        AssignedByType assignedByType,
        Guid? assignedByUser,
        bool isBatch,
        long driverPayAmount,
        DateTime now)
        : base(id)
    {
        OrderId = orderId;
        DriverId = driverId;
        OfferId = offerId;
        AssignedByType = assignedByType;
        AssignedByUser = assignedByUser;
        IsBatch = isBatch;
        DriverPayAmount = driverPayAmount;
        Status = AssignmentStatus.Assigned;
        AssignedAt = now;
        AddLocalEvent(new DriverNotificationEto(driverId, DriverNotificationKind.TaskAssigned, orderId, offerId, null));
    }

    public bool IsActive => Status is AssignmentStatus.Assigned or AssignmentStatus.PickedUp;

    /* BR-033: before pickup only (the driver gives up, ops reassign, or the order is cancelled). */
    internal void Release(string reason, DateTime now)
    {
        Status = AssignmentStatus.Released;
        ReleasedAt = now;
        ReleaseReason = reason;
        AddLocalEvent(new DriverNotificationEto(DriverId, DriverNotificationKind.TaskReleased, OrderId, OfferId, null));
    }
}
