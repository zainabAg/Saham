using System;
using Saham.Entities.Ledger;
using Saham.Entities.Payments;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Settlements;

/* SRS settlements.settlement_type. */
public enum SettlementType
{
    DriverDaily = 0,          // UC-23: the driver's cash of one operating day
    DriverWeeklyPayout = 1,   // BR-068: earnings up to Wednesday, paid on Thursday
    RestaurantWeekly = 2      // BR-040 / UC-24: Saturday 04:00 → Saturday 04:00
}

/* DRAFT → CONFIRMED → PAID (we paid) / COLLECTED (they paid) / CLOSED (nothing to pay). */
public enum SettlementStatus
{
    Draft = 0,
    Confirmed = 1,
    Paid = 2,
    Collected = 3,
    Closed = 4
}

/* A statement (SRS table settlements). Each ledger line of the party's account belongs to exactly
 * one statement (journal_lines.settlement_id, set once — the only update the ledger trigger allows),
 * so a movement that arrives after the close simply falls into the next period (UC-24 E2).
 * UK (type, party, period start): running the close twice never makes a second statement (E1).
 * After CONFIRMED the figures never change. */
public class Settlement : FullAuditedAggregateRoot<Guid>
{
    public const int ReferenceMaxLength = 128;

    public SettlementType SettlementType { get; private set; }

    public LedgerPartyType PartyType { get; private set; }

    public Guid PartyId { get; private set; }

    public DateOnly PeriodStart { get; private set; }

    /* Inclusive operating day. */
    public DateOnly PeriodEnd { get; private set; }

    /* Account balance before the period (the carried amount of a driver's day). */
    public long OpeningBalance { get; private set; }

    public long TotalDebits { get; private set; }

    public long TotalCredits { get; private set; }

    /* Restaurants and driver earnings: credits − debits (positive = the company pays).
     * Driver's day: opening + debits − credits (cash still due, carried to the next day). */
    public long NetAmount { get; private set; }

    public int OrdersCount { get; private set; }

    public SettlementStatus Status { get; private set; }

    public Guid? ConfirmedBy { get; private set; }

    public DateTime? ConfirmedAt { get; private set; }

    public string? PaidReference { get; private set; }

    public ManualPaymentChannel? PaidChannel { get; private set; }

    public Guid? PaidBy { get; private set; }

    public DateTime? PaidAt { get; private set; }

    public Guid? PayoutEntryId { get; private set; }

    protected Settlement()
    {
    }

    internal Settlement(Guid id, SettlementType type, LedgerPartyType partyType, Guid partyId, DateOnly periodStart, DateOnly periodEnd)
        : base(id)
    {
        SettlementType = type;
        PartyType = partyType;
        PartyId = partyId;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        Status = SettlementStatus.Draft;
    }

    internal void SetTotals(long opening, long debits, long credits, int ordersCount)
    {
        EnsureStatus(SettlementStatus.Draft);
        OpeningBalance = opening;
        TotalDebits = debits;
        TotalCredits = credits;
        OrdersCount = ordersCount;
        NetAmount = SettlementType == SettlementType.DriverDaily ? opening + debits - credits : credits - debits;
    }

    /* Nothing to pay: a driver's day, or a zero / negative earnings statement, closes on confirmation. */
    public void Confirm(Guid by, DateTime now)
    {
        EnsureStatus(SettlementStatus.Draft);
        ConfirmedBy = by;
        ConfirmedAt = now;
        Status = SettlementType == SettlementType.DriverDaily ||
                 NetAmount == 0 ||
                 (SettlementType == SettlementType.DriverWeeklyPayout && NetAmount < 0)
            ? SettlementStatus.Closed
            : SettlementStatus.Confirmed;
    }

    public bool NeedsPayout => Status == SettlementStatus.Confirmed && NetAmount > 0;

    public bool NeedsCollection => Status == SettlementStatus.Confirmed && NetAmount < 0;

    internal void MarkPaid(Guid by, string reference, ManualPaymentChannel channel, Guid entryId, DateTime now)
    {
        if (!NeedsPayout)
        {
            throw new BusinessException(SahamErrorCodes.InvalidSettlementStatus).WithData("status", Status.ToString());
        }

        SetPayment(by, reference, channel, entryId, now);
        Status = SettlementStatus.Paid;
    }

    /* UC-24 A1: the restaurant owed us (negative net) and paid. */
    internal void MarkCollected(Guid by, string reference, ManualPaymentChannel channel, Guid entryId, DateTime now)
    {
        if (!NeedsCollection)
        {
            throw new BusinessException(SahamErrorCodes.InvalidSettlementStatus).WithData("status", Status.ToString());
        }

        SetPayment(by, reference, channel, entryId, now);
        Status = SettlementStatus.Collected;
    }

    private void SetPayment(Guid by, string reference, ManualPaymentChannel channel, Guid entryId, DateTime now)
    {
        PaidReference = Check.NotNullOrWhiteSpace(reference, nameof(reference), ReferenceMaxLength);
        PaidChannel = channel;
        PaidBy = by;
        PaidAt = now;
        PayoutEntryId = entryId;
    }

    private void EnsureStatus(SettlementStatus expected)
    {
        if (Status != expected)
        {
            throw new BusinessException(SahamErrorCodes.InvalidSettlementStatus).WithData("status", Status.ToString());
        }
    }
}
