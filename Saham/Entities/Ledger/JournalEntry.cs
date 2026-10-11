using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace Saham.Entities.Ledger;

/* One balanced posting (SRS tables journal_entries / journal_lines). Never updated or deleted:
 * database triggers reject UPDATE / DELETE, and a mistake is corrected by a reversing entry
 * (BR-043, SOD-04). The idempotency key (e.g. "order:{id}:delivered") is unique, so the same
 * economic event is posted once no matter how often it is retried (SRS 13.8 §3). */
public class JournalEntry : AggregateRoot<Guid>
{
    public const int DescriptionMaxLength = 256;

    public JournalEntryType EntryType { get; private set; }

    public JournalSourceType SourceType { get; private set; }

    public Guid SourceId { get; private set; }

    public string IdempotencyKey { get; private set; }

    public string Description { get; private set; }

    /* Operating day (ends at 04:00 Baghdad, ASM-09); daily and weekly statements group by it. */
    public DateOnly BusinessDate { get; private set; }

    public DateTime PostedAt { get; private set; }

    /* Empty = posted by the system. */
    public Guid? CreatedBy { get; private set; }

    public Guid? ReversesEntryId { get; private set; }

    public ICollection<JournalLine> Lines { get; private set; }

    protected JournalEntry()
    {
        IdempotencyKey = null!;
        Description = null!;
        Lines = new List<JournalLine>();
    }

    internal JournalEntry(
        Guid id,
        JournalEntryType entryType,
        JournalSourceType sourceType,
        Guid sourceId,
        string idempotencyKey,
        string description,
        DateOnly businessDate,
        DateTime postedAt,
        Guid? createdBy,
        IEnumerable<(Guid AccountId, long Debit, long Credit, Guid? OrderId)> lines,
        Func<Guid> newId,
        Guid? reversesEntryId = null)
        : base(id)
    {
        EntryType = entryType;
        SourceType = sourceType;
        SourceId = sourceId;
        IdempotencyKey = Check.NotNullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey), 128);
        Description = Check.Length(description, nameof(description), DescriptionMaxLength) ?? string.Empty;
        BusinessDate = businessDate;
        PostedAt = postedAt;
        CreatedBy = createdBy;
        ReversesEntryId = reversesEntryId;
        Lines = new List<JournalLine>();

        foreach (var (accountId, debit, credit, orderId) in lines.Where(l => l.Debit != 0 || l.Credit != 0))
        {
            Lines.Add(new JournalLine(newId(), id, accountId, debit, credit, orderId));
        }

        // Double entry (BR-044): at least two lines, each one-sided and positive, totals equal.
        if (Lines.Count < 2 ||
            Lines.Any(l => l.Debit < 0 || l.Credit < 0 || (l.Debit > 0) == (l.Credit > 0)) ||
            Lines.Sum(l => l.Debit) != Lines.Sum(l => l.Credit))
        {
            throw new BusinessException(SahamErrorCodes.UnbalancedEntry).WithData("key", idempotencyKey);
        }
    }

    public long Total => Lines.Sum(l => l.Debit);
}
