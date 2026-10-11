using System;
using Volo.Abp.Domain.Entities;

namespace Saham.Entities.Ledger;

/* One side of a posting. OrderId answers "every dinar of this order" in one query;
 * SettlementId ties a line to exactly one statement (set once, later, by settlements). */
public class JournalLine : Entity<Guid>
{
    public Guid EntryId { get; private set; }

    public Guid AccountId { get; private set; }

    public long Debit { get; private set; }

    public long Credit { get; private set; }

    public Guid? OrderId { get; private set; }

    public Guid? SettlementId { get; private set; }

    protected JournalLine()
    {
    }

    internal JournalLine(Guid id, Guid entryId, Guid accountId, long debit, long credit, Guid? orderId)
        : base(id)
    {
        EntryId = entryId;
        AccountId = accountId;
        Debit = debit;
        Credit = credit;
        OrderId = orderId;
    }
}
