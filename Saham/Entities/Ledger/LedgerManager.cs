using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Saham.Entities.Customers;
using Saham.Entities.Geography;
using Saham.Settings;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Settings;
using Volo.Abp.Uow;

namespace Saham.Entities.Ledger;

/* The only writer of journal entries (SRS 14.4, M09). */
public class LedgerManager : DomainService
{
    private readonly IRepository<LedgerAccount, Guid> _accountRepository;
    private readonly IRepository<JournalEntry, Guid> _entryRepository;
    private readonly IRepository<JournalLine, Guid> _lineRepository;
    private readonly IRepository<City, Guid> _cityRepository;
    private readonly ISettingProvider _settingProvider;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly CustomerBalanceLock _customerBalanceLock;

    public LedgerManager(
        IRepository<LedgerAccount, Guid> accountRepository,
        IRepository<JournalEntry, Guid> entryRepository,
        IRepository<JournalLine, Guid> lineRepository,
        IRepository<City, Guid> cityRepository,
        ISettingProvider settingProvider,
        IUnitOfWorkManager unitOfWorkManager,
        CustomerBalanceLock customerBalanceLock)
    {
        _customerBalanceLock = customerBalanceLock;
        _accountRepository = accountRepository;
        _entryRepository = entryRepository;
        _lineRepository = lineRepository;
        _cityRepository = cityRepository;
        _settingProvider = settingProvider;
        _unitOfWorkManager = unitOfWorkManager;
    }

    // ------------------------------------------------------------------ accounts

    public Task<Guid> CompanyAccountAsync(LedgerAccountCode code) => GetOrCreateAccountIdAsync(code, null);

    public Task<Guid> PartyAccountAsync(LedgerAccountCode code, Guid partyId) => GetOrCreateAccountIdAsync(code, partyId);

    private async Task<Guid> GetOrCreateAccountIdAsync(LedgerAccountCode code, Guid? partyId)
    {
        var existing = await FindAccountAsync(code, partyId);
        if (existing != null)
        {
            return existing.Id;
        }

        // Created in its own transaction so two requests creating the same account cannot both
        // fail: the loser of the unique-index race simply reads the winner's row.
        try
        {
            using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
            var account = new LedgerAccount(GuidGenerator.Create(), code, LedgerAccount.PartyOf(code), partyId, Clock.Now);
            await _accountRepository.InsertAsync(account);
            await uow.CompleteAsync();
            return account.Id;
        }
        catch (DbUpdateException)
        {
            return (await FindAccountAsync(code, partyId))!.Id;
        }
    }

    private Task<LedgerAccount?> FindAccountAsync(LedgerAccountCode code, Guid? partyId) =>
        _accountRepository.FindAsync(a => a.Code == code && a.PartyId == partyId);

    // ------------------------------------------------------------------ dates

    /* ASM-09: the operating day ends at 04:00 local time, so a 02:00 delivery belongs to the previous day. */
    public async Task<DateOnly> BusinessDateAsync(DateTime utc, Guid? cityId = null)
    {
        var endHour = await _settingProvider.GetAsync<int>(SahamSettings.Cash.BusinessDayEndHour);
        var city = cityId.HasValue
            ? await _cityRepository.GetAsync(cityId.Value)
            : await _cityRepository.FirstOrDefaultAsync(c => c.IsActive);
        var local = city?.ToLocalTime(utc) ?? utc.AddHours(3);
        return DateOnly.FromDateTime(local.AddHours(-endHour));
    }

    // ------------------------------------------------------------------ posting

    /* Posts a balanced entry once. A repeated key returns the entry already posted (no second posting).
     * An entry touching a customer's CUSTOMER_BALANCE needs that customer's balance lock (BR-064/071):
     * otherwise an order being created could plan against the balance this entry changes. */
    public async Task<JournalEntry> PostAsync(
        JournalEntryType entryType,
        JournalSourceType sourceType,
        Guid sourceId,
        string idempotencyKey,
        string description,
        DateOnly businessDate,
        IEnumerable<(Guid AccountId, long Debit, long Credit, Guid? OrderId)> lines,
        Guid? createdBy = null)
    {
        var existing = await _entryRepository.FindAsync(e => e.IdempotencyKey == idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        var lineList = lines.ToList();
        await EnsureCustomerBalanceLockedAsync(lineList.Select(l => l.AccountId).Distinct().ToList());

        var entry = new JournalEntry(GuidGenerator.Create(), entryType, sourceType, sourceId, idempotencyKey,
            description, businessDate, Clock.Now, createdBy, lineList, GuidGenerator.Create);

        // Flushed at once (same transaction) so balance checks right after the posting see it,
        // e.g. the cash cap after a delivery. The balance trigger still runs at commit.
        return await _entryRepository.InsertAsync(entry, autoSave: true);
    }

    private async Task EnsureCustomerBalanceLockedAsync(List<Guid> accountIds)
    {
        var accounts = await _accountRepository.GetQueryableAsync();
        var customers = await AsyncExecuter.ToListAsync(accounts
            .Where(a => accountIds.Contains(a.Id) && a.Code == LedgerAccountCode.CustomerBalance && a.PartyId != null)
            .Select(a => a.PartyId!.Value));
        foreach (var customerId in customers)
        {
            _customerBalanceLock.EnsureHeld(customerId);
        }
    }

    // ------------------------------------------------------------------ balances

    /* Σ debit − Σ credit. Assets and expenses are naturally positive; liabilities and revenue negative. */
    public async Task<long> BalanceAsync(Guid accountId)
    {
        var lines = await _lineRepository.GetQueryableAsync();
        return await AsyncExecuter.SumAsync(lines.Where(l => l.AccountId == accountId), l => l.Debit - l.Credit);
    }

    public async Task<long> PartyBalanceAsync(LedgerAccountCode code, Guid partyId)
    {
        var account = await FindAccountAsync(code, partyId);
        return account == null ? 0 : await BalanceAsync(account.Id);
    }

    /* Balances of one account code for many parties in one query (lists for the accountant). */
    public async Task<Dictionary<Guid, long>> PartyBalancesAsync(LedgerAccountCode code, IReadOnlyCollection<Guid> partyIds)
    {
        var accounts = await _accountRepository.GetQueryableAsync();
        var lines = await _lineRepository.GetQueryableAsync();
        var rows = await AsyncExecuter.ToListAsync(
            from l in lines
            join a in accounts on l.AccountId equals a.Id
            where a.Code == code && a.PartyId != null && partyIds.Contains(a.PartyId.Value)
            group l by a.PartyId into g
            select new { PartyId = g.Key!.Value, Balance = g.Sum(x => x.Debit - x.Credit) });

        return rows.ToDictionary(r => r.PartyId, r => r.Balance);
    }
}
