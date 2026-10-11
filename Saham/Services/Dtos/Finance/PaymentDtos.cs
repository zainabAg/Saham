using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Saham.Entities.Ledger;
using Saham.Entities.Payments;
using Saham.Entities.Settlements;
using Volo.Abp.Application.Dtos;

namespace Saham.Services.Dtos.Finance;

// ------------------------------------------------------------------ refunds (UC-22)

public class RefundDto : FullAuditedEntityDto<Guid>
{
    public Guid OrderId { get; set; }

    public long OrderNumber { get; set; }

    public Guid PaymentId { get; set; }

    public long Amount { get; set; }

    public RefundReason Reason { get; set; }

    public RefundStatus Status { get; set; }

    public int Attempts { get; set; }

    public DateTime? NextAttemptAt { get; set; }

    public string? LastError { get; set; }

    public string? ProviderRefundId { get; set; }

    public string? ResolutionReference { get; set; }

    public ManualPaymentChannel? ResolutionChannel { get; set; }

    public Guid? ResolvedBy { get; set; }

    public DateTime? CompletedAt { get; set; }
}

public class GetRefundsInput : PagedResultRequestDto
{
    public RefundStatus? Status { get; set; }

    public Guid? OrderId { get; set; }
}

public class ResolveRefundDto
{
    /* Transfer number or receipt — the proof the money went back (UC-22 E3). */
    [Required]
    [StringLength(Refund.ReferenceMaxLength)]
    public string Reference { get; set; } = string.Empty;

    public ManualPaymentChannel Channel { get; set; } = ManualPaymentChannel.Bank;
}

// ------------------------------------------------------------------ settlements (UC-23, UC-24, BR-068)

public class SettlementDto : FullAuditedEntityDto<Guid>
{
    public SettlementType SettlementType { get; set; }

    public LedgerPartyType PartyType { get; set; }

    public Guid PartyId { get; set; }

    public string? PartyName { get; set; }

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public long OpeningBalance { get; set; }

    public long TotalDebits { get; set; }

    public long TotalCredits { get; set; }

    public long NetAmount { get; set; }

    public int OrdersCount { get; set; }

    public SettlementStatus Status { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    public string? PaidReference { get; set; }

    public ManualPaymentChannel? PaidChannel { get; set; }

    public DateTime? PaidAt { get; set; }

    /* Filled by Get: the movements of the statement. */
    public List<SettlementLineDto> Lines { get; set; } = new();
}

public class SettlementLineDto
{
    public JournalEntryType EntryType { get; set; }

    public string Description { get; set; } = string.Empty;

    public DateOnly BusinessDate { get; set; }

    public Guid? OrderId { get; set; }

    public long Debit { get; set; }

    public long Credit { get; set; }
}

public class GetSettlementsInput : PagedResultRequestDto
{
    public SettlementType? SettlementType { get; set; }

    public SettlementStatus? Status { get; set; }

    public Guid? PartyId { get; set; }

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }
}

public class GenerateSettlementsDto
{
    public SettlementType SettlementType { get; set; }

    /* A closed period: a day for DRIVER_DAILY; the configured first day of the week otherwise. */
    public DateOnly PeriodStart { get; set; }
}

public class PaySettlementDto
{
    [Required]
    [StringLength(Settlement.ReferenceMaxLength)]
    public string Reference { get; set; } = string.Empty;

    public ManualPaymentChannel Channel { get; set; } = ManualPaymentChannel.Bank;
}
