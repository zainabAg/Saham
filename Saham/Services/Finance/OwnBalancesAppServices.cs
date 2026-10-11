using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Saham.Entities.Customers;
using Saham.Entities.Dispatch;
using Saham.Entities.Ledger;
using Saham.Entities.Orders;
using Saham.Identity;
using Saham.Permissions;
using Saham.Services.Dtos.Finance;
using Saham.Settings;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Settings;
using Volo.Abp.Users;

namespace Saham.Services.Finance;

public interface IDriverSummaryAppService : IApplicationService
{
    Task<DriverTodaySummaryDto> GetTodayAsync();
}

/* REQ-DRV-11/14: the driver sees today's orders, today's earnings and the cash he holds. */
[Authorize(SahamPermissions.Dispatch.OfferRespond)]
public class DriverSummaryAppService : SahamAppService, IDriverSummaryAppService
{
    private readonly ICurrentScope _currentScope;
    private readonly LedgerManager _ledger;
    private readonly IRepository<DeliveryAssignment, Guid> _assignmentRepository;
    private readonly IRepository<JournalLine, Guid> _lineRepository;
    private readonly IRepository<JournalEntry, Guid> _entryRepository;
    private readonly IRepository<LedgerAccount, Guid> _accountRepository;

    public DriverSummaryAppService(
        ICurrentScope currentScope,
        LedgerManager ledger,
        IRepository<DeliveryAssignment, Guid> assignmentRepository,
        IRepository<JournalLine, Guid> lineRepository,
        IRepository<JournalEntry, Guid> entryRepository,
        IRepository<LedgerAccount, Guid> accountRepository)
    {
        _currentScope = currentScope;
        _ledger = ledger;
        _assignmentRepository = assignmentRepository;
        _lineRepository = lineRepository;
        _entryRepository = entryRepository;
        _accountRepository = accountRepository;
    }

    public async Task<DriverTodaySummaryDto> GetTodayAsync()
    {
        var driver = await _currentScope.GetDriverAsync();
        var today = await _ledger.BusinessDateAsync(Clock.Now, driver.CityId);

        // Today's pay entries for this driver = today's orders and earnings.
        var payable = await _accountRepository.FindAsync(a => a.Code == LedgerAccountCode.DriverPayable && a.PartyId == driver.Id);
        var earnedToday = 0L;
        if (payable != null)
        {
            var lines = await _lineRepository.GetQueryableAsync();
            var entries = await _entryRepository.GetQueryableAsync();
            earnedToday = await AsyncExecuter.SumAsync(
                from l in lines join e in entries on l.EntryId equals e.Id
                where l.AccountId == payable.Id && e.BusinessDate == today && e.EntryType == JournalEntryType.DriverPay
                select l.Credit);
        }

        var dayStart = Clock.Now.AddHours(-24);
        var finished = await _assignmentRepository.GetListAsync(a => a.DriverId == driver.Id &&
            (a.Status == AssignmentStatus.Delivered || a.Status == AssignmentStatus.Failed) &&
            (a.DeliveredAt >= dayStart || a.ReleasedAt >= dayStart));
        var finishedToday = await FilterTodayAsync(finished, today, driver.CityId);

        return new DriverTodaySummaryDto
        {
            BusinessDate = today,
            DeliveredToday = finishedToday.Count(a => a.Status == AssignmentStatus.Delivered),
            NotReceivedToday = finishedToday.Count(a => a.Status == AssignmentStatus.Failed),
            EarnedToday = earnedToday,
            EarningsDue = -await _ledger.PartyBalanceAsync(LedgerAccountCode.DriverPayable, driver.Id),
            CashInHand = await _ledger.PartyBalanceAsync(LedgerAccountCode.DriverCash, driver.Id),
            CashCap = await SettingProvider.GetAsync<long>(SahamSettings.Cash.DriverCap),
            Float = await _ledger.PartyBalanceAsync(LedgerAccountCode.DriverFloat, driver.Id),
            CashBlocked = driver.CashBlocked
        };
    }

    private async Task<System.Collections.Generic.List<DeliveryAssignment>> FilterTodayAsync(
        System.Collections.Generic.List<DeliveryAssignment> assignments, DateOnly today, Guid cityId)
    {
        var result = new System.Collections.Generic.List<DeliveryAssignment>();
        foreach (var a in assignments)
        {
            if (await _ledger.BusinessDateAsync(a.DeliveredAt ?? a.ReleasedAt!.Value, cityId) == today)
            {
                result.Add(a);
            }
        }

        return result;
    }
}

public interface IRestaurantFinanceAppService : IApplicationService
{
    Task<RestaurantOwnBalanceDto> GetAsync();
}

/* DER-RST-08: manager and cashier see what the company owes the restaurant (or the reverse). */
[Authorize(SahamPermissions.Settlements.View)]
public class RestaurantFinanceAppService : SahamAppService, IRestaurantFinanceAppService
{
    private readonly ICurrentScope _currentScope;
    private readonly LedgerManager _ledger;
    private readonly IRepository<Order, Guid> _orderRepository;

    public RestaurantFinanceAppService(ICurrentScope currentScope, LedgerManager ledger, IRepository<Order, Guid> orderRepository)
    {
        _currentScope = currentScope;
        _ledger = ledger;
        _orderRepository = orderRepository;
    }

    public async Task<RestaurantOwnBalanceDto> GetAsync()
    {
        var restaurantId = await _currentScope.GetRestaurantIdAsync();
        var today = await _ledger.BusinessDateAsync(Clock.Now);
        var since = Clock.Now.AddHours(-30);
        var recent = await _orderRepository.GetListAsync(o => o.RestaurantId == restaurantId &&
            o.Status == OrderStatus.Delivered && o.DeliveredAt >= since);

        var todays = new System.Collections.Generic.List<Order>();
        foreach (var o in recent)
        {
            if (await _ledger.BusinessDateAsync(o.DeliveredAt!.Value, o.CityId) == today)
            {
                todays.Add(o);
            }
        }

        return new RestaurantOwnBalanceDto
        {
            RestaurantId = restaurantId,
            Balance = -await _ledger.PartyBalanceAsync(LedgerAccountCode.RestaurantPayable, restaurantId),
            BusinessDate = today,
            DeliveredToday = todays.Count,
            NetToday = todays.Sum(o => o.RestaurantNetAmount)
        };
    }
}

public interface ICustomerBalanceAppService : IApplicationService
{
    Task<CustomerBalanceDto> GetAsync();
}

/* REQ-CUS-19/20: the customer sees his credit or his debt, and whether cash is disabled. */
[Authorize(SahamPermissions.Orders.Create)]
public class CustomerBalanceAppService : SahamAppService, ICustomerBalanceAppService
{
    private readonly ICustomerBalanceProvider _balanceProvider;
    private readonly IRepository<CustomerProfile, Guid> _profileRepository;

    public CustomerBalanceAppService(ICustomerBalanceProvider balanceProvider, IRepository<CustomerProfile, Guid> profileRepository)
    {
        _balanceProvider = balanceProvider;
        _profileRepository = profileRepository;
    }

    public async Task<CustomerBalanceDto> GetAsync()
    {
        var userId = CurrentUser.GetId();
        var balance = await _balanceProvider.GetBalanceAsync(userId);
        var profile = await _profileRepository.FindAsync(userId);

        return new CustomerBalanceDto
        {
            Balance = balance,
            Credit = Math.Max(0, balance),
            Debt = Math.Max(0, -balance),
            CashDisabled = profile?.CashDisabled ?? false,
            NoShowCount = profile?.NoShowCount ?? 0
        };
    }
}

public interface ICustomerAdminAppService : IApplicationService
{
    Task<CustomerBalanceDto> GetAsync(Guid customerId);

    Task<CustomerBalanceDto> LiftCashBanAsync(Guid customerId);
}

/* customer.cash_ban.lift ✱: only the owner lifts the cash ban after two no-shows (BR-065, REC-09). */
[Authorize(SahamPermissions.Customers.CashBanLift)]
public class CustomerAdminAppService : SahamAppService, ICustomerAdminAppService
{
    private readonly ICustomerBalanceProvider _balanceProvider;
    private readonly IRepository<CustomerProfile, Guid> _profileRepository;

    public CustomerAdminAppService(ICustomerBalanceProvider balanceProvider, IRepository<CustomerProfile, Guid> profileRepository)
    {
        _balanceProvider = balanceProvider;
        _profileRepository = profileRepository;
    }

    public async Task<CustomerBalanceDto> GetAsync(Guid customerId)
    {
        var profile = await _profileRepository.GetAsync(customerId);
        var balance = await _balanceProvider.GetBalanceAsync(customerId);
        return new CustomerBalanceDto
        {
            Balance = balance,
            Credit = Math.Max(0, balance),
            Debt = Math.Max(0, -balance),
            CashDisabled = profile.CashDisabled,
            NoShowCount = profile.NoShowCount
        };
    }

    public async Task<CustomerBalanceDto> LiftCashBanAsync(Guid customerId)
    {
        var profile = await _profileRepository.GetAsync(customerId);
        profile.LiftCashBan(CurrentUser.GetId(), Clock.Now);
        await _profileRepository.UpdateAsync(profile);
        return await GetAsync(customerId);
    }
}
