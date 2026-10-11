using System;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Saham.Entities.Customers;

/* The customer's in-app balance (SRS 12.2, CUSTOMER_BALANCE): positive = credit
 * (complaint compensation, change owed), negative = debt (cash order not received).
 * Always computed from the ledger, never stored (LedgerCustomerBalanceProvider). */
public interface ICustomerBalanceProvider
{
    Task<long> GetBalanceAsync(Guid customerId);
}
