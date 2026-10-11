using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Support;

/* DER-OPS-02 incident queue. Stored as strings (D3), so new values are safe to add. */
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
    DeliveredFarFromAddress = 8,
    CashOverdue = 9,
    CashCapReached = 10,
    DeliveryCodeSmsFailed = 11,
    ScheduledRestaurantClosed = 12,
    RefundFailed = 13,          // UC-22 E2: the accountant's queue
    PaymentAmountMismatch = 14, // UC-05 E5
    CouponRequested = 15,       // BR-072: a restaurant asks the owner for a coupon
    ContactRequest = 16,        // TD-03: the driver asks ops to call the customer
    Other = 99
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
 * has a partial unique index while not resolved, so the same problem is never opened twice.
 * DER-OPS-02: each incident shows its status, who handles it, the action taken, a note and when it was closed. */
public class Incident : CreationAuditedAggregateRoot<Guid>
{
    public const int NoteMaxLength = 1024;
    public const int ResolutionMaxLength = 512;

    /* Financial incidents are also the accountant's queue (DER-OPS-02). */
    public static readonly IncidentType[] FinancialTypes =
    {
        IncidentType.CashOverdue, IncidentType.CashCapReached, IncidentType.RefundFailed, IncidentType.PaymentAmountMismatch
    };

    public IncidentType IncidentType { get; private set; }

    public IncidentSeverity Severity { get; private set; }

    public Guid? OrderId { get; private set; }

    public Guid? DriverId { get; private set; }

    public Guid? RestaurantId { get; private set; }

    public IncidentStatus Status { get; private set; }

    public Guid? AssignedTo { get; private set; }

    public DateTime OpenedAt { get; private set; }

    public DateTime? ResolvedAt { get; private set; }

    public Guid? ResolvedBy { get; private set; }

    /* The action taken. */
    public string? Resolution { get; private set; }

    /* Free text kept by ops while following up (calls made, who was told...). */
    public string? Note { get; private set; }

    public string DedupeKey { get; private set; }

    protected Incident()
    {
        DedupeKey = null!;
    }

    internal Incident(Guid id, IncidentType type, IncidentSeverity severity, string dedupeKey,
        Guid? orderId, Guid? driverId, Guid? restaurantId, DateTime now, string? note = null)
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
        SetNote(note);
        RaiseChanged();
    }

    public bool IsFinancial => Array.IndexOf(FinancialTypes, IncidentType) >= 0;

    public void Acknowledge(Guid userId)
    {
        if (Status == IncidentStatus.Open)
        {
            Status = IncidentStatus.Acknowledged;
            AssignedTo ??= userId;
            RaiseChanged();
        }
    }

    /* Hand the incident to a colleague (or take it). */
    public void AssignTo(Guid userId)
    {
        EnsureNotResolved();
        AssignedTo = userId;
        if (Status == IncidentStatus.Open)
        {
            Status = IncidentStatus.Acknowledged;
        }

        RaiseChanged();
    }

    public void UpdateNote(string? note)
    {
        EnsureNotResolved();
        SetNote(note);
        RaiseChanged();
    }

    public void Resolve(string resolution, Guid? by, DateTime now)
    {
        if (Status == IncidentStatus.Resolved)
        {
            return;
        }

        Status = IncidentStatus.Resolved;
        Resolution = Check.NotNullOrWhiteSpace(resolution, nameof(resolution), ResolutionMaxLength);
        ResolvedBy = by;
        ResolvedAt = now;
        RaiseChanged();
    }

    private void SetNote(string? note)
    {
        Note = note.IsNullOrWhiteSpace() ? null : Check.Length(note!.Trim(), nameof(note), NoteMaxLength);
    }

    private void EnsureNotResolved()
    {
        if (Status == IncidentStatus.Resolved)
        {
            throw new BusinessException(SahamErrorCodes.IncidentAlreadyResolved);
        }
    }

    /* Sent to the ops board after commit (IncidentRealtimeNotifier). */
    private void RaiseChanged()
    {
        AddLocalEvent(new IncidentChangedEto(Id, IncidentType, Status, Severity, OrderId));
    }
}

public record IncidentChangedEto(Guid IncidentId, IncidentType IncidentType, IncidentStatus Status, IncidentSeverity Severity, Guid? OrderId);
