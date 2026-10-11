using System;
using Volo.Abp.Domain.Entities;

namespace Saham.Entities.Ledger;

/* An account of the ledger (SRS table ledger_accounts). Company accounts have no party;
 * driver / restaurant / customer accounts are created on first use.
 * Unique on (Code, PartyType, PartyId) with NULLs not distinct. There is no balance column:
 * a balance is always the sum of the account's journal lines (SRS 12.1 §2). */
public class LedgerAccount : AggregateRoot<Guid>
{
    public LedgerAccountCode Code { get; private set; }

    public LedgerAccountType AccountType { get; private set; }

    public LedgerPartyType PartyType { get; private set; }

    public Guid? PartyId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreationTime { get; private set; }

    protected LedgerAccount()
    {
    }

    internal LedgerAccount(Guid id, LedgerAccountCode code, LedgerPartyType partyType, Guid? partyId, DateTime now)
        : base(id)
    {
        Code = code;
        AccountType = TypeOf(code);
        PartyType = partyType;
        PartyId = partyId;
        IsActive = true;
        CreationTime = now;
    }

    public static LedgerAccountType TypeOf(LedgerAccountCode code) => code switch
    {
        LedgerAccountCode.CashOnHand or LedgerAccountCode.CompanyBank or LedgerAccountCode.WalletClearing
            or LedgerAccountCode.DriverCash or LedgerAccountCode.DriverFloat => LedgerAccountType.Asset,
        LedgerAccountCode.CommissionRevenue or LedgerAccountCode.DeliveryFeeRevenue => LedgerAccountType.Revenue,
        LedgerAccountCode.DriverPayExpense or LedgerAccountCode.DiscountExpense or LedgerAccountCode.WalletFeeExpense
            or LedgerAccountCode.AdjustmentExpense or LedgerAccountCode.OperatingExpense => LedgerAccountType.Expense,
        _ => LedgerAccountType.Liability
    };

    public static LedgerPartyType PartyOf(LedgerAccountCode code) => code switch
    {
        LedgerAccountCode.DriverCash or LedgerAccountCode.DriverFloat or LedgerAccountCode.DriverPayable => LedgerPartyType.Driver,
        LedgerAccountCode.RestaurantPayable => LedgerPartyType.Restaurant,
        LedgerAccountCode.CustomerBalance => LedgerPartyType.Customer,
        _ => LedgerPartyType.Company
    };
}
