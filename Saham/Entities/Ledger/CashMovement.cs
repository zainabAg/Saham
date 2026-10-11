using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Ledger;

/* Physical cash between a driver and the office (SRS table cash_movements, UC-23):
 * end-of-shift handover counted by the accountant, and the change float.
 * ExpectedAmount is the driver's cash balance at that moment; the difference with the counted
 * Amount stays visible as the driver's liability (EX-20) — numbers are never "fixed" to match. */
public class CashMovement : CreationAuditedAggregateRoot<Guid>
{
    public const int NoteMaxLength = 512;

    public Guid DriverId { get; private set; }

    public CashMovementType MovementType { get; private set; }

    public long Amount { get; private set; }

    public long? ExpectedAmount { get; private set; }

    public DateOnly BusinessDate { get; private set; }

    public Guid ReceivedBy { get; private set; }

    public string? Note { get; private set; }

    public Guid JournalEntryId { get; private set; }

    protected CashMovement()
    {
    }

    internal CashMovement(Guid id, Guid driverId, CashMovementType movementType, long amount, long? expectedAmount,
        DateOnly businessDate, Guid receivedBy, string? note, Guid journalEntryId)
        : base(id)
    {
        DriverId = driverId;
        MovementType = movementType;
        Amount = amount;
        ExpectedAmount = expectedAmount;
        BusinessDate = businessDate;
        ReceivedBy = receivedBy;
        Note = note;
        JournalEntryId = journalEntryId;
    }

    public long? Difference => ExpectedAmount.HasValue ? ExpectedAmount.Value - Amount : null;
}
