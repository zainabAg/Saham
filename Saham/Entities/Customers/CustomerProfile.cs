using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Customers;

/* One row per customer; Id = IdentityUser.Id (SRS table customer_profiles).
 * The customer's balance and debt are NOT stored here: they are computed
 * from the CUSTOMER_BALANCE ledger account (SRS 12.2). */
public class CustomerProfile : AuditedAggregateRoot<Guid>
{
    public int NoShowCount { get; private set; }

    public bool CashDisabled { get; private set; }

    public DateTime? CashDisabledAt { get; private set; }

    public Guid? CashBanLiftedBy { get; private set; }

    public DateTime? CashBanLiftedAt { get; private set; }

    protected CustomerProfile()
    {
    }

    public CustomerProfile(Guid userId)
        : base(userId)
    {
    }

    /* BR-065: the cash ban becomes permanent once the no-show count reaches the threshold. */
    public void RegisterNoShow(int cashBanThreshold, DateTime now)
    {
        NoShowCount++;
        if (!CashDisabled && NoShowCount >= cashBanThreshold)
        {
            CashDisabled = true;
            CashDisabledAt = now;
        }
    }

    /* Only the owner can lift the ban (customer.cash_ban.lift). */
    public void LiftCashBan(Guid liftedBy, DateTime now)
    {
        CashDisabled = false;
        CashBanLiftedBy = liftedBy;
        CashBanLiftedAt = now;
    }
}
