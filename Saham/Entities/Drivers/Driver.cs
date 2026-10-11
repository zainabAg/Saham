using System;
using NetTopologySuite.Geometries;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Drivers;

/* A delivery driver (SRS table drivers); Id = IdentityUser.Id.
 * ActiveOrderCount is maintained in the same transaction as assignments and is capped
 * at 2 by a CHECK constraint, so even a bug cannot give a driver a third order (BR-030). */
public class Driver : AuditedAggregateRoot<Guid>
{
    public const int VehiclePlateMaxLength = 32;
    public const int ReviewNoteMaxLength = 512;

    public Guid CityId { get; private set; }

    public DriverReviewStatus ReviewStatus { get; private set; }

    public string? ReviewNote { get; private set; }

    public Guid? DocsReviewedBy { get; private set; }

    public Guid? ApprovedBy { get; private set; }

    public DateTime? ReviewedAt { get; private set; }

    public DriverAvailability Availability { get; private set; }

    public int ActiveOrderCount { get; private set; }

    public DriverCashBlock? CashBlocked { get; private set; }

    public DateTime? CashBlockedAt { get; private set; }

    /* Called by ops when the driver is unreachable with an order (REC-07). */
    public string? EmergencyContactPhone { get; private set; }

    /* Change float carried by the driver (REC-04). */
    public long FloatAmount { get; private set; }

    public string? VehiclePlate { get; private set; }

    // Location changes every few seconds: kept out of the entity change history.
    [DisableAuditing]
    public Point? LastLocation { get; private set; }

    [DisableAuditing]
    public DateTime? LastLocationAt { get; private set; }

    /* Tie-breaker for dispatch: the driver waiting longest goes first (BR-027). */
    public DateTime? LastTaskCompletedAt { get; private set; }

    protected Driver()
    {
    }

    internal Driver(Guid userId, Guid cityId, string? vehiclePlate, string? emergencyContactPhone, long floatAmount)
        : base(userId)
    {
        CityId = cityId;
        ReviewStatus = DriverReviewStatus.PendingReview;
        Availability = DriverAvailability.Offline;
        FloatAmount = floatAmount;
        UpdateDetails(vehiclePlate, emergencyContactPhone);
    }

    public void UpdateDetails(string? vehiclePlate, string? emergencyContactPhone)
    {
        VehiclePlate = vehiclePlate.IsNullOrWhiteSpace() ? null : Check.Length(vehiclePlate!.Trim(), nameof(vehiclePlate), VehiclePlateMaxLength);
        EmergencyContactPhone = emergencyContactPhone;
    }

    // ------------------------------------------------------------ review (BR-051, SOD-07)

    /* All three documents accepted by ops: waiting for the owner. */
    internal void MarkDocsAccepted(Guid reviewedBy, DateTime now)
    {
        if (ReviewStatus is DriverReviewStatus.PendingReview)
        {
            ReviewStatus = DriverReviewStatus.DocsAccepted;
            DocsReviewedBy = reviewedBy;
            ReviewedAt = now;
            ReviewNote = null;
        }
    }

    /* A document was rejected or re-uploaded: back to review. */
    internal void MarkDocsPending()
    {
        if (ReviewStatus is DriverReviewStatus.DocsAccepted)
        {
            ReviewStatus = DriverReviewStatus.PendingReview;
        }
    }

    public void Approve(Guid ownerId, DateTime now)
    {
        if (ReviewStatus is not (DriverReviewStatus.DocsAccepted or DriverReviewStatus.Suspended))
        {
            throw new BusinessException(SahamErrorCodes.DriverDocumentsIncomplete);
        }

        ReviewStatus = DriverReviewStatus.Approved;
        ApprovedBy = ownerId;
        ReviewedAt = now;
        ReviewNote = null;
    }

    public void Reject(string note, DateTime now) => Close(DriverReviewStatus.Rejected, note, now);

    public void Suspend(string note, DateTime now) => Close(DriverReviewStatus.Suspended, note, now);

    private void Close(DriverReviewStatus status, string note, DateTime now)
    {
        if (note.IsNullOrWhiteSpace())
        {
            throw new BusinessException(SahamErrorCodes.ReasonRequired);
        }

        ReviewStatus = status;
        ReviewNote = Check.Length(note.Trim(), nameof(note), ReviewNoteMaxLength);
        ReviewedAt = now;
        Availability = DriverAvailability.Offline; // a rejected or suspended driver gets nothing (BR-051)
    }

    // ------------------------------------------------------------ work

    public bool IsApproved => ReviewStatus == DriverReviewStatus.Approved;

    public void SetAvailability(bool available)
    {
        if (available && !IsApproved)
        {
            throw new BusinessException(SahamErrorCodes.DriverNotApproved);
        }

        // BR-057: the app cannot go offline with an order in hand.
        if (!available && ActiveOrderCount > 0)
        {
            throw new BusinessException(SahamErrorCodes.DriverHasActiveOrders);
        }

        Availability = available ? DriverAvailability.Available : DriverAvailability.Offline;
    }

    /* Keeps only the newest point; late points from the offline queue do not move the driver back. */
    public void UpdateLocation(Point location, DateTime recordedAt)
    {
        if (LastLocationAt.HasValue && recordedAt <= LastLocationAt.Value)
        {
            return;
        }

        LastLocation = location;
        LastLocationAt = recordedAt;
    }

    public bool HasFreshLocation(DateTime now, int freshSeconds) =>
        LastLocationAt.HasValue && LastLocationAt.Value >= now.AddSeconds(-freshSeconds);

    internal void AddActiveOrder(int maxActiveOrders)
    {
        if (ActiveOrderCount >= maxActiveOrders)
        {
            throw new BusinessException(SahamErrorCodes.DriverAtCapacity);
        }

        ActiveOrderCount++;
    }

    internal void RemoveActiveOrder(DateTime now, bool completed)
    {
        if (ActiveOrderCount > 0)
        {
            ActiveOrderCount--;
        }

        if (completed)
        {
            LastTaskCompletedAt = now;
        }
    }

    /* BR-066 / BR-067: no cash orders until the cash is handed over. */
    public void BlockCash(DriverCashBlock reason, DateTime now)
    {
        if (CashBlocked == null || (CashBlocked == DriverCashBlock.Cap && reason == DriverCashBlock.Overdue))
        {
            CashBlocked = reason;
            CashBlockedAt = now;
        }
    }

    public void UnblockCash()
    {
        CashBlocked = null;
        CashBlockedAt = null;
    }

    /* Demo/seed data only: an approved driver without the review workflow. */
    internal void ApproveForSeed(Guid approverId, DateTime now)
    {
        ReviewStatus = DriverReviewStatus.Approved;
        ApprovedBy = approverId;
        DocsReviewedBy = approverId;
        ReviewedAt = now;
    }
}
