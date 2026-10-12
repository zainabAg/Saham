using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Saham.Settings;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Settings;
using Volo.Abp.Uow;

namespace Saham.Entities.Customers;

/* The one lock for everything that reads or changes a customer's CUSTOMER_BALANCE together with
 * the reservations (BR-064, BR-071): order creation, the order's ledger postings, modifications.
 * A PostgreSQL advisory TRANSACTION lock, so it protects across servers and is held until commit
 * or rollback. Under READ COMMITTED every statement after it sees what the previous holder committed.
 *
 * Lock order: whatever rows the caller already updated (order, driver, assignment) → this lock →
 * ledger, coupons, reservations. Never take it inside a requiresNew unit of work while the parent
 * may hold it: the child is another connection and would wait for its own parent. */
public class CustomerBalanceLock : ITransientDependency
{
    // Two-key form pg_advisory_xact_lock(int, int): the first key keeps these locks apart from any other use.
    public const int LockNamespace = 0x5348_4231; // "SHB1"

    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IRepository<CustomerBalanceReservation, Guid> _repository;
    private readonly ISettingProvider _settingProvider;

    public CustomerBalanceLock(
        IUnitOfWorkManager unitOfWorkManager,
        IRepository<CustomerBalanceReservation, Guid> repository,
        ISettingProvider settingProvider)
    {
        _unitOfWorkManager = unitOfWorkManager;
        _repository = repository;
        _settingProvider = settingProvider;
    }

    public async Task AcquireAsync(Guid customerId)
    {
        var uow = _unitOfWorkManager.Current;
        if (uow == null || !uow.Options.IsTransactional)
        {
            throw new AbpException("The customer balance lock needs a transactional unit of work.");
        }

        if (IsHeld(customerId))
        {
            return; // re-entrant within one transaction (PostgreSQL advisory locks are, too)
        }

        var database = (await _repository.GetDbContextAsync()).Database;
        if (database.CurrentTransaction == null)
        {
            throw new AbpException("The customer balance lock needs an open database transaction.");
        }

        // A bounded wait: a stuck holder becomes a 409 "try again", never a hung request. It must be longer
        // than one order creation (≈15 s against a remote database), or a normal second order fails as busy.
        var timeoutSeconds = await _settingProvider.GetAsync<int>(SahamSettings.Orders.BalanceLockTimeoutSeconds);
        try
        {
            // set_config(..., true) = SET LOCAL, for this transaction only
            await database.ExecuteSqlRawAsync("SELECT set_config('lock_timeout', {0}, true)", $"{Math.Max(1, timeoutSeconds)}s");
            await database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0}, {1})", LockNamespace, KeyOf(customerId));
            await database.ExecuteSqlRawAsync("SELECT set_config('lock_timeout', '0', true)");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            throw new BusinessException(SahamErrorCodes.CustomerBalanceBusy);
        }

        uow.Items[ItemKey(customerId)] = true;
    }

    public bool IsHeld(Guid customerId) =>
        _unitOfWorkManager.Current?.Items.ContainsKey(ItemKey(customerId)) == true;

    public void EnsureHeld(Guid customerId)
    {
        if (!IsHeld(customerId))
        {
            throw new AbpException($"The customer balance lock of {customerId} is not held by this transaction.");
        }
    }

    /* Stable across processes and versions (unlike string.GetHashCode): the 16 bytes folded to 32 bits.
     * Two customers sharing a key only wait for each other; correctness is unaffected. */
    public static int KeyOf(Guid customerId)
    {
        var bytes = customerId.ToByteArray();
        return BitConverter.ToInt32(bytes, 0) ^ BitConverter.ToInt32(bytes, 4) ^
               BitConverter.ToInt32(bytes, 8) ^ BitConverter.ToInt32(bytes, 12);
    }

    private static string ItemKey(Guid customerId) => "Saham.CustomerBalanceLock:" + customerId.ToString("N");
}
