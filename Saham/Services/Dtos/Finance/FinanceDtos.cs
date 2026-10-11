using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Saham.Entities.Drivers;
using Saham.Entities.Ledger;
using Saham.Entities.Support;
using Volo.Abp.Application.Dtos;

namespace Saham.Services.Dtos.Finance;

// ------------------------------------------------------------------ cash desk (accountant)

public class RecordHandoverDto
{
    [Required]
    public Guid DriverId { get; set; }

    /* What the accountant actually counted (EX-20: never adjusted to match). */
    [Range(1, long.MaxValue)]
    public long Amount { get; set; }

    [StringLength(CashMovement.NoteMaxLength)]
    public string? Note { get; set; }
}

public class MoveFloatDto
{
    [Required]
    public Guid DriverId { get; set; }

    [Range(1, long.MaxValue)]
    public long Amount { get; set; }

    [StringLength(CashMovement.NoteMaxLength)]
    public string? Note { get; set; }
}

public class GetCashMovementsInput : PagedResultRequestDto
{
    public Guid? DriverId { get; set; }

    public DateOnly? BusinessDate { get; set; }
}

public class CashMovementDto : CreationAuditedEntityDto<Guid>
{
    public Guid DriverId { get; set; }

    public CashMovementType MovementType { get; set; }

    public long Amount { get; set; }

    public long? ExpectedAmount { get; set; }

    /* Expected − counted; stays on the driver (EX-20). */
    public long? Difference { get; set; }

    public DateOnly BusinessDate { get; set; }

    public Guid ReceivedBy { get; set; }

    public string? Note { get; set; }

    /* The driver's cash after this movement. */
    public long CashInHandAfter { get; set; }

    public DriverCashBlock? CashBlockedAfter { get; set; }
}

// ------------------------------------------------------------------ balances ("at any moment I know", S-12)

public class DriverBalanceDto
{
    public Guid DriverId { get; set; }

    public string? Name { get; set; }

    public string PhoneNumber { get; set; } = string.Empty;

    /* Cash collected and not handed over. */
    public long CashInHand { get; set; }

    /* Earnings owed to the driver (paid every Thursday). */
    public long EarningsDue { get; set; }

    public long Float { get; set; }

    public DriverCashBlock? CashBlocked { get; set; }

    public DateTime? CashBlockedAt { get; set; }
}

public class RestaurantBalanceDto
{
    public Guid RestaurantId { get; set; }

    public string Name { get; set; } = string.Empty;

    /* Positive: we owe the restaurant. Negative: the restaurant owes us. */
    public long Balance { get; set; }
}

public class JournalLineDto
{
    public LedgerAccountCode AccountCode { get; set; }

    public LedgerPartyType PartyType { get; set; }

    public Guid? PartyId { get; set; }

    public long Debit { get; set; }

    public long Credit { get; set; }
}

public class JournalEntryDto : EntityDto<Guid>
{
    public JournalEntryType EntryType { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateOnly BusinessDate { get; set; }

    public DateTime PostedAt { get; set; }

    public List<JournalLineDto> Lines { get; set; } = new();
}

// ------------------------------------------------------------------ own views

/* The driver's day (REQ-DRV-11/14): orders today, earned today, cash in hand. */
public class DriverTodaySummaryDto
{
    public DateOnly BusinessDate { get; set; }

    public int DeliveredToday { get; set; }

    public int NotReceivedToday { get; set; }

    public long EarnedToday { get; set; }

    public long EarningsDue { get; set; }

    public long CashInHand { get; set; }

    public long CashCap { get; set; }

    public long Float { get; set; }

    public DriverCashBlock? CashBlocked { get; set; }
}

/* The restaurant's money with the company (DER-RST-08). */
public class RestaurantOwnBalanceDto
{
    public Guid RestaurantId { get; set; }

    /* Positive: the company owes the restaurant. */
    public long Balance { get; set; }

    public DateOnly BusinessDate { get; set; }

    public int DeliveredToday { get; set; }

    public long NetToday { get; set; }
}

/* REQ-CUS-19/20: credit from compensation / change, or debt from a cash order not received. */
public class CustomerBalanceDto
{
    /* Positive = credit, negative = debt. */
    public long Balance { get; set; }

    public long Credit { get; set; }

    public long Debt { get; set; }

    public bool CashDisabled { get; set; }

    public int NoShowCount { get; set; }
}

// ------------------------------------------------------------------ ops: delivery failure

public class RecordContactAttemptDto
{
    public ContactChannel Channel { get; set; } = ContactChannel.OpsPhone;

    public ContactOutcome Outcome { get; set; }

    [StringLength(512)]
    public string? Note { get; set; }
}

public class MarkFailedDto
{
    [StringLength(512)]
    public string? Note { get; set; }
}
