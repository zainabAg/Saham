using System;
using System.Threading.Tasks;
using Saham.Entities.Customers;
using Volo.Abp.DependencyInjection;

namespace Saham.Entities.Ledger;

/* The customer's balance from the ledger (replaces the phase-3 zero provider):
 * CUSTOMER_BALANCE credit = in-app credit (positive), debit = debt (negative).
 * Quotes add the debt to the next order and use the credit automatically (BR-064, BR-071). */
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(ICustomerBalanceProvider))]
public class LedgerCustomerBalanceProvider : ICustomerBalanceProvider, ITransientDependency
{
    private readonly LedgerManager _ledger;

    public LedgerCustomerBalanceProvider(LedgerManager ledger)
    {
        _ledger = ledger;
    }

    public async Task<long> GetBalanceAsync(Guid customerId) =>
        -await _ledger.PartyBalanceAsync(LedgerAccountCode.CustomerBalance, customerId);
}
