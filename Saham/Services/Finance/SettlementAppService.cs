using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Ledger;
using Saham.Entities.Restaurants;
using Saham.Entities.Settlements;
using Saham.Identity;
using Saham.Infrastructure.Idempotency;
using Saham.Permissions;
using Saham.Services.Dtos.Finance;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Saham.Services.Finance;

public interface ISettlementAppService : IApplicationService
{
    Task<PagedResultDto<SettlementDto>> GetListAsync(GetSettlementsInput input);

    Task<SettlementDto> GetAsync(Guid id);

    Task<ListResultDto<SettlementDto>> GenerateAsync(GenerateSettlementsDto input);

    Task<SettlementDto> ConfirmAsync(Guid id);

    Task<SettlementDto> MarkPaidAsync(Guid id, PaySettlementDto input);

    Task<SettlementDto> MarkCollectedAsync(Guid id, PaySettlementDto input);
}

/* Statements (UC-23, UC-24, BR-068):
 *   settlement.view            — the accountant and the owner see all; a restaurant manager / cashier
 *                                sees their restaurant's weekly statements; a driver sees his own;
 *   settlement.generate_confirm— build a closed period now (the 04:00 close does it by itself) and confirm;
 *                                a confirmed statement never changes (409 on a second confirmation);
 *   settlement.mark_paid       — record the payment made outside the system, with its reference. */
[Authorize(SahamPermissions.Settlements.View)]
public class SettlementAppService : SahamAppService, ISettlementAppService
{
    private readonly IRepository<Settlement, Guid> _settlementRepository;
    private readonly IRepository<JournalLine, Guid> _lineRepository;
    private readonly IRepository<JournalEntry, Guid> _entryRepository;
    private readonly IRepository<Restaurant, Guid> _restaurantRepository;
    private readonly IRepository<IdentityUser, Guid> _userRepository;
    private readonly SettlementManager _settlementManager;
    private readonly LedgerManager _ledger;
    private readonly ICurrentScope _currentScope;

    public SettlementAppService(
        IRepository<Settlement, Guid> settlementRepository,
        IRepository<JournalLine, Guid> lineRepository,
        IRepository<JournalEntry, Guid> entryRepository,
        IRepository<Restaurant, Guid> restaurantRepository,
        IRepository<IdentityUser, Guid> userRepository,
        SettlementManager settlementManager,
        LedgerManager ledger,
        ICurrentScope currentScope)
    {
        _settlementRepository = settlementRepository;
        _lineRepository = lineRepository;
        _entryRepository = entryRepository;
        _restaurantRepository = restaurantRepository;
        _userRepository = userRepository;
        _settlementManager = settlementManager;
        _ledger = ledger;
        _currentScope = currentScope;
    }

    public async Task<PagedResultDto<SettlementDto>> GetListAsync(GetSettlementsInput input)
    {
        var (ownParty, ownTypes) = await GetOwnScopeAsync();
        var queryable = (await _settlementRepository.GetQueryableAsync())
            .WhereIf(ownParty.HasValue, s => s.PartyId == ownParty && ownTypes!.Contains(s.SettlementType))
            .WhereIf(input.SettlementType.HasValue, s => s.SettlementType == input.SettlementType)
            .WhereIf(input.Status.HasValue, s => s.Status == input.Status)
            .WhereIf(input.PartyId.HasValue, s => s.PartyId == input.PartyId)
            .WhereIf(input.From.HasValue, s => s.PeriodStart >= input.From)
            .WhereIf(input.To.HasValue, s => s.PeriodStart <= input.To);

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var settlements = await AsyncExecuter.ToListAsync(queryable
            .OrderByDescending(s => s.PeriodStart)
            .ThenBy(s => s.SettlementType)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));

        return new PagedResultDto<SettlementDto>(totalCount, await MapAsync(settlements));
    }

    public async Task<SettlementDto> GetAsync(Guid id)
    {
        var settlement = await GetInScopeAsync(id);
        var dto = (await MapAsync(new List<Settlement> { settlement })).Single();

        var lines = await _lineRepository.GetQueryableAsync();
        var entries = await _entryRepository.GetQueryableAsync();
        dto.Lines = await AsyncExecuter.ToListAsync(
            from l in lines
            join e in entries on l.EntryId equals e.Id
            where l.SettlementId == settlement.Id
            orderby e.BusinessDate, e.PostedAt
            select new SettlementLineDto
            {
                EntryType = e.EntryType,
                Description = e.Description,
                BusinessDate = e.BusinessDate,
                OrderId = l.OrderId,
                Debit = l.Debit,
                Credit = l.Credit
            });
        return dto;
    }

    [Authorize(SahamPermissions.Settlements.GenerateConfirm)]
    public async Task<ListResultDto<SettlementDto>> GenerateAsync(GenerateSettlementsDto input)
    {
        var today = await _ledger.BusinessDateAsync(Clock.Now);
        var created = await _settlementManager.GenerateAsync(input.SettlementType, input.PeriodStart, today);
        return new ListResultDto<SettlementDto>(await MapAsync(created));
    }

    [Idempotent]
    [Authorize(SahamPermissions.Settlements.GenerateConfirm)]
    public async Task<SettlementDto> ConfirmAsync(Guid id)
    {
        var settlement = await _settlementRepository.GetAsync(id);
        settlement.Confirm(CurrentUser.GetId(), Clock.Now);

        await _settlementRepository.UpdateAsync(settlement, autoSave: true);
        return await GetAsync(id);
    }

    [Idempotent]
    [Authorize(SahamPermissions.Settlements.MarkPaid)]
    public async Task<SettlementDto> MarkPaidAsync(Guid id, PaySettlementDto input)
    {
        var settlement = await _settlementRepository.GetAsync(id);
        if (settlement.SettlementType == SettlementType.DriverWeeklyPayout)
        {
            // UC-31 / REQ-FIN-16: paying drivers' Thursday earnings is its own permission (driver.payout_weekly).
            await AuthorizationService.CheckAsync(SahamPermissions.Drivers.PayoutWeekly);
        }

        await _settlementManager.MarkPaidAsync(settlement, CurrentUser.GetId(), input.Reference, input.Channel);

        await _settlementRepository.UpdateAsync(settlement, autoSave: true);
        return await GetAsync(id);
    }

    [Idempotent]
    [Authorize(SahamPermissions.Settlements.MarkPaid)]
    public async Task<SettlementDto> MarkCollectedAsync(Guid id, PaySettlementDto input)
    {
        var settlement = await _settlementRepository.GetAsync(id);
        await _settlementManager.MarkCollectedAsync(settlement, CurrentUser.GetId(), input.Reference, input.Channel);

        await _settlementRepository.UpdateAsync(settlement, autoSave: true);
        return await GetAsync(id);
    }

    /* Restaurant staff and drivers see only their own statements; company staff see all. */
    private async Task<(Guid? PartyId, SettlementType[]? Types)> GetOwnScopeAsync()
    {
        if (await _currentScope.IsRestaurantStaffAsync())
        {
            return (await _currentScope.GetRestaurantIdAsync(), new[] { SettlementType.RestaurantWeekly });
        }

        if (CurrentUser.IsInRole(SahamRoles.Driver))
        {
            return ((await _currentScope.GetDriverAsync()).Id, new[] { SettlementType.DriverDaily, SettlementType.DriverWeeklyPayout });
        }

        if (CurrentUser.IsInRole(SahamRoles.Accountant) || CurrentUser.IsInRole(SahamRoles.Owner))
        {
            return (null, null);
        }

        throw new AbpAuthorizationException();
    }

    private async Task<Settlement> GetInScopeAsync(Guid id)
    {
        var settlement = await _settlementRepository.GetAsync(id);
        var (party, types) = await GetOwnScopeAsync();
        if (party.HasValue && (settlement.PartyId != party || !types!.Contains(settlement.SettlementType)))
        {
            throw new EntityNotFoundException(typeof(Settlement), id);
        }

        return settlement;
    }

    private async Task<List<SettlementDto>> MapAsync(List<Settlement> settlements)
    {
        var ids = settlements.Select(s => s.PartyId).Distinct().ToList();
        var restaurants = (await _restaurantRepository.GetListAsync(r => ids.Contains(r.Id), includeDetails: false)).ToDictionary(r => r.Id, r => r.Name.Ar);
        var users = (await _userRepository.GetListAsync(u => ids.Contains(u.Id))).ToDictionary(u => u.Id, u => u.Name ?? u.UserName);

        return settlements.Select(s =>
        {
            var dto = ObjectMapper.Map<Settlement, SettlementDto>(s);
            dto.PartyName = s.PartyType == LedgerPartyType.Restaurant ? restaurants.GetValueOrDefault(s.PartyId) : users.GetValueOrDefault(s.PartyId);
            return dto;
        }).ToList();
    }
}
