using System;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Saham.Entities.Customers;

/* The customer's in-app balance (SRS 12.2, CUSTOMER_BALANCE): positive = credit
 * (complaint compensation), negative = debt (cash order not received).
 * It is always computed from the ledger, never stored. The ledger arrives in phase 7;
 * until then every customer has a zero balance. */
public interface ICustomerBalanceProvider
{
    Task<long> GetBalanceAsync(Guid customerId);
}

public class ZeroCustomerBalanceProvider : ICustomerBalanceProvider, ITransientDependency
{
    public Task<long> GetBalanceAsync(Guid customerId) => Task.FromResult(0L);
}
