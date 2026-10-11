using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Drivers;
using Saham.Entities.Ledger;
using Saham.Entities.Restaurants;
using Saham.Identity;
using Saham.Permissions;
using Saham.Services.Dtos.Finance;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Saham.Services.Finance;

/* "At any moment I know what every driver holds, what we owe every restaurant and what it owes us"
 * (S-12, BO-04): balances computed live from the ledger. Accountant and owner only; drivers,
 * restaurants and customers have their own scoped endpoints. */
[Authorize(SahamPermissions.Ledger.BalancesView)]
public class BalancesAppService : SahamAppService, IBalancesAppService
{
    private readonly IRepository<Driver, Guid> _driverRepository;
    private readonly IRepository<Restaurant, Guid> _restaurantRepository;
    private readonly IRepository<IdentityUser, Guid> _userRepository;
    private readonly IRepository<JournalLine, Guid> _lineRepository;
    private readonly IRepository<JournalEntry, Guid> _entryRepository;
    private readonly IRepository<LedgerAccount, Guid> _accountRepository;
    private readonly LedgerManager _ledger;

    public BalancesAppService(
        IRepository<Driver, Guid> driverRepository,
        IRepository<Restaurant, Guid> restaurantRepository,
        IRepository<IdentityUser, Guid> userRepository,
        IRepository<JournalLine, Guid> lineRepository,
        IRepository<JournalEntry, Guid> entryRepository,
        IRepository<LedgerAccount, Guid> accountRepository,
        LedgerManager ledger)
    {
        _driverRepository = driverRepository;
        _restaurantRepository = restaurantRepository;
        _userRepository = userRepository;
        _lineRepository = lineRepository;
        _entryRepository = entryRepository;
        _accountRepository = accountRepository;
        _ledger = ledger;
    }

    public async Task<ListResultDto<DriverBalanceDto>> GetDriversAsync()
    {
        CheckFinanceStaff();
        var drivers = await _driverRepository.GetListAsync(d => d.ReviewStatus != DriverReviewStatus.Rejected);
        var ids = drivers.Select(d => d.Id).ToList();
        var users = (await _userRepository.GetListAsync(u => ids.Contains(u.Id))).ToDictionary(u => u.Id);
        var cash = await _ledger.PartyBalancesAsync(LedgerAccountCode.DriverCash, ids);
        var payable = await _ledger.PartyBalancesAsync(LedgerAccountCode.DriverPayable, ids);
        var floats = await _ledger.PartyBalancesAsync(LedgerAccountCode.DriverFloat, ids);

        return new ListResultDto<DriverBalanceDto>(drivers
            .Select(d => new DriverBalanceDto
            {
                DriverId = d.Id,
                Name = users.GetValueOrDefault(d.Id)?.Name,
                PhoneNumber = users.GetValueOrDefault(d.Id)?.PhoneNumber ?? string.Empty,
                CashInHand = cash.GetValueOrDefault(d.Id),
                EarningsDue = -payable.GetValueOrDefault(d.Id),
                Float = floats.GetValueOrDefault(d.Id),
                CashBlocked = d.CashBlocked,
                CashBlockedAt = d.CashBlockedAt
            })
            .OrderByDescending(d => d.CashInHand)
            .ToList());
    }

    public async Task<ListResultDto<RestaurantBalanceDto>> GetRestaurantsAsync()
    {
        CheckFinanceStaff();
        var restaurants = await _restaurantRepository.GetListAsync();
        var balances = await _ledger.PartyBalancesAsync(LedgerAccountCode.RestaurantPayable, restaurants.Select(r => r.Id).ToList());

        return new ListResultDto<RestaurantBalanceDto>(restaurants
            .Select(r => new RestaurantBalanceDto
            {
                RestaurantId = r.Id,
                Name = r.Name.Localize(),
                Balance = -balances.GetValueOrDefault(r.Id)
            })
            .OrderByDescending(r => r.Balance)
            .ToList());
    }

    /* "Every dinar of this order" in one query (SRS 13.5, journal_lines.order_id). */
    public async Task<ListResultDto<JournalEntryDto>> GetOrderEntriesAsync(Guid orderId)
    {
        CheckFinanceStaff();
        var entryIds = (await _lineRepository.GetListAsync(l => l.OrderId == orderId)).Select(l => l.EntryId).Distinct().ToList();
        var entries = await AsyncExecuter.ToListAsync((await _entryRepository.WithDetailsAsync(e => e.Lines))
            .Where(e => entryIds.Contains(e.Id)).OrderBy(e => e.PostedAt));
        var accountIds = entries.SelectMany(e => e.Lines).Select(l => l.AccountId).Distinct().ToList();
        var accounts = (await _accountRepository.GetListAsync(a => accountIds.Contains(a.Id))).ToDictionary(a => a.Id);

        return new ListResultDto<JournalEntryDto>(entries.Select(e => new JournalEntryDto
        {
            Id = e.Id,
            EntryType = e.EntryType,
            IdempotencyKey = e.IdempotencyKey,
            Description = e.Description,
            BusinessDate = e.BusinessDate,
            PostedAt = e.PostedAt,
            Lines = e.Lines.OrderByDescending(l => l.Debit).Select(l => new JournalLineDto
            {
                AccountCode = accounts[l.AccountId].Code,
                PartyType = accounts[l.AccountId].PartyType,
                PartyId = accounts[l.AccountId].PartyId,
                Debit = l.Debit,
                Credit = l.Credit
            }).ToList()
        }).ToList());
    }

    /* ledger.balances.view is also held (scoped) by drivers, restaurants and customers. */
    private void CheckFinanceStaff()
    {
        if (!(CurrentUser.IsInRole(SahamRoles.Accountant) || CurrentUser.IsInRole(SahamRoles.Owner)))
        {
            throw new AbpAuthorizationException();
        }
    }
}
