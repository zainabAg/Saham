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

    public PickedUpByType? PickedUpByType { get; private set; }

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

    /* "I'm at the restaurant" (ASM-37): within 200 m; the basis of half pay if the order is then cancelled. */
    internal void MarkArrivedAtRestaurant(DateTime eventTime)
    {
        EnsureStatus(AssignmentStatus.Assigned);
        ArrivedAtRestaurantAt ??= eventTime;
    }

    internal void MarkPickedUp(PickedUpByType by, DateTime eventTime)
    {
        EnsureStatus(AssignmentStatus.Assigned);
        Status = AssignmentStatus.PickedUp;
        PickedUpAt = eventTime;
        PickedUpByType = by;
        ArrivedAtRestaurantAt ??= eventTime;
    }

    /* Start of the 10-minute wait before "customer not answering" can be reported (BR-063). */
    internal void MarkArrivedAtCustomer(DateTime eventTime)
    {
        EnsureStatus(AssignmentStatus.PickedUp);
        ArrivedAtCustomerAt ??= eventTime;
    }

    /* BR-035: delivering far from the address is flagged for review, never blocked. */
    internal void MarkDelivered(DateTime eventTime, Point? driverLocation, bool geofenceFlag)
    {
        EnsureStatus(AssignmentStatus.PickedUp);
        Status = AssignmentStatus.Delivered;
        DeliveredAt = eventTime;
        DeliveryLocation = driverLocation;
        GeofenceFlag = geofenceFlag;
    }

    /* An action from the driver's offline queue that arrives after the task was taken away (SRS 10.7 §4). */
    public void EnsureActiveFor(Guid driverId)
    {
        if (DriverId != driverId)
        {
            throw new Volo.Abp.Domain.Entities.EntityNotFoundException(typeof(DeliveryAssignment), Id);
        }

        if (!IsActive)
        {
            throw new Volo.Abp.BusinessException(SahamErrorCodes.AssignmentReleased).WithData("status", Status.ToString());
        }
    }

    private void EnsureStatus(AssignmentStatus expected)
    {
        if (Status != expected)
        {
            throw new Volo.Abp.BusinessException(Status == AssignmentStatus.Released ? SahamErrorCodes.AssignmentReleased : SahamErrorCodes.InvalidStateTransition)
                .WithData("from", Status.ToString())
                .WithData("to", expected.ToString());
        }
    }

    /* BR-033: before pickup only (the driver gives up, ops reassign, or the order is cancelled). */
    internal void Release(string reason, DateTime now)
    {
        Status = AssignmentStatus.Released;
        ReleasedAt = now;
        ReleaseReason = reason;
        AddLocalEvent(new DriverNotificationEto(DriverId, DriverNotificationKind.TaskReleased, OrderId, OfferId, null));
    }
}
