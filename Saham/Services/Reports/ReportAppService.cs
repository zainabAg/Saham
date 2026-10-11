using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using MiniExcelLibs;
using Saham.Entities.Customers;
using Saham.Entities.Dispatch;
using Saham.Entities.Geography;
using Saham.Entities.Ledger;
using Saham.Entities.Orders;
using Saham.Entities.Payments;
using Saham.Entities.Restaurants;
using Saham.Entities.Settlements;
using Saham.Identity;
using Saham.Permissions;
using Saham.Services.Dtos.Reports;
using Saham.Settings;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Settings;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Saham.Services.Reports;

public interface IReportAppService : IApplicationService
{
    Task<ReportTableDto> GetAsync(ReportCode report, ReportInput input);

    Task<IRemoteStreamContent> ExportAsync(ExportReportInput input);
}

/* Reports (SRS 17.2). Shared rules: "completed" = DELIVERED; periods are operating days (BR-061);
 * every figure comes from the orders and the ledger, never from numbers kept by hand.
 * Who sees what:
 *   reports.operational (ops, owner, accountant): RPT-01/02/03/07/08/12;
 *   reports.financial (owner, accountant): RPT-04/09/11/12/13;
 *   a restaurant sees RPT-01/02/07/08 for itself only, a driver RPT-03/13 for himself only.
 * Export to Excel is a POST, so every export is written to the audit log (SRS 5.5): a data sheet with
 * the same columns (numbers as numbers) and a "Criteria" sheet (period, filters, time, user). */
[Authorize]
public class ReportAppService : SahamAppService, IReportAppService
{
    private static readonly ReportCode[] FinancialReports =
        { ReportCode.MonthlyProfit, ReportCode.Settlements, ReportCode.Refunds, ReportCode.DriverWeekly };

    private static readonly ReportCode[] RestaurantReports =
        { ReportCode.RestaurantSales, ReportCode.DeliveryTimes, ReportCode.OrderCounts, ReportCode.Cancellations };

    private static readonly ReportCode[] DriverReports = { ReportCode.DriverPerformance, ReportCode.DriverWeekly };

    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IRepository<DeliveryAssignment, Guid> _assignmentRepository;
    private readonly IRepository<DeliveryOffer, Guid> _offerRepository;
    private readonly IRepository<CashMovement, Guid> _cashRepository;
    private readonly IRepository<JournalLine, Guid> _lineRepository;
    private readonly IRepository<JournalEntry, Guid> _entryRepository;
    private readonly IRepository<LedgerAccount, Guid> _accountRepository;
    private readonly IRepository<Settlement, Guid> _settlementRepository;
    private readonly IRepository<Refund, Guid> _refundRepository;
    private readonly IRepository<CustomerProfile, Guid> _profileRepository;
    private readonly IRepository<Restaurant, Guid> _restaurantRepository;
    private readonly IRepository<City, Guid> _cityRepository;
    private readonly IRepository<IdentityUser, Guid> _userRepository;
    private readonly ICurrentScope _currentScope;

    public ReportAppService(
        IRepository<Order, Guid> orderRepository,
        IRepository<DeliveryAssignment, Guid> assignmentRepository,
        IRepository<DeliveryOffer, Guid> offerRepository,
        IRepository<CashMovement, Guid> cashRepository,
        IRepository<JournalLine, Guid> lineRepository,
        IRepository<JournalEntry, Guid> entryRepository,
        IRepository<LedgerAccount, Guid> accountRepository,
        IRepository<Settlement, Guid> settlementRepository,
        IRepository<Refund, Guid> refundRepository,
        IRepository<CustomerProfile, Guid> profileRepository,
        IRepository<Restaurant, Guid> restaurantRepository,
        IRepository<City, Guid> cityRepository,
        IRepository<IdentityUser, Guid> userRepository,
        ICurrentScope currentScope)
    {
        _orderRepository = orderRepository;
        _assignmentRepository = assignmentRepository;
        _offerRepository = offerRepository;
        _cashRepository = cashRepository;
        _lineRepository = lineRepository;
        _entryRepository = entryRepository;
        _accountRepository = accountRepository;
        _settlementRepository = settlementRepository;
        _refundRepository = refundRepository;
        _profileRepository = profileRepository;
        _restaurantRepository = restaurantRepository;
        _cityRepository = cityRepository;
        _userRepository = userRepository;
        _currentScope = currentScope;
    }

    public async Task<ReportTableDto> GetAsync(ReportCode report, ReportInput input)
    {
        await ApplyScopeAsync(report, input);
        if (input.To < input.From || input.To.DayNumber - input.From.DayNumber > 366)
        {
            throw new BusinessException(SahamErrorCodes.InvalidSettlementPeriod).WithData("startDay", "-");
        }

        var (fromUtc, toUtc) = await UtcRangeAsync(input.From, input.To);
        var table = report switch
        {
            ReportCode.RestaurantSales => await RestaurantSalesAsync(input, fromUtc, toUtc),
            ReportCode.DeliveryTimes => await DeliveryTimesAsync(input, fromUtc, toUtc),
            ReportCode.DriverPerformance => await DriverPerformanceAsync(input, fromUtc, toUtc),
            ReportCode.MonthlyProfit => await MonthlyProfitAsync(input, fromUtc, toUtc),
            ReportCode.OrderCounts => await OrderCountsAsync(input, fromUtc, toUtc),
            ReportCode.Cancellations => await CancellationsAsync(input, fromUtc, toUtc),
            ReportCode.Settlements => await SettlementsAsync(input),
            ReportCode.Refunds => await RefundsAsync(input, fromUtc, toUtc),
            ReportCode.CustomerBalances => await CustomerBalancesAsync(),
            ReportCode.DriverWeekly => await DriverWeeklyAsync(input),
            _ => throw new BusinessException(SahamErrorCodes.UnknownReport)
        };

        table.Report = report;
        table.Criteria["From"] = input.From.ToString("yyyy-MM-dd");
        table.Criteria["To"] = input.To.ToString("yyyy-MM-dd");
        if (input.RestaurantId.HasValue) table.Criteria["RestaurantId"] = input.RestaurantId.Value.ToString();
        if (input.DriverId.HasValue) table.Criteria["DriverId"] = input.DriverId.Value.ToString();
        return table;
    }

    public async Task<IRemoteStreamContent> ExportAsync(ExportReportInput input)
    {
        var table = await GetAsync(input.Report, input);
        var criteria = table.Criteria.Select(c => new Dictionary<string, object?> { ["Name"] = c.Key, ["Value"] = c.Value }).ToList();
        criteria.Add(new() { ["Name"] = "Report", ["Value"] = $"{table.Report} — {table.Title}" });
        criteria.Add(new() { ["Name"] = "GeneratedAtUtc", ["Value"] = Clock.Now.ToString("yyyy-MM-dd HH:mm:ss") });
        criteria.Add(new() { ["Name"] = "GeneratedBy", ["Value"] = CurrentUser.UserName });
        foreach (var s in table.Summary)
        {
            criteria.Add(new() { ["Name"] = s.Key, ["Value"] = s.Value });
        }

        var stream = new MemoryStream();
        await stream.SaveAsAsync(new Dictionary<string, object>
        {
            ["Data"] = table.Rows.Count > 0 ? table.Rows : new List<Dictionary<string, object?>> { table.Columns.ToDictionary(c => c, _ => (object?)null) },
            ["Criteria"] = criteria
        });
        stream.Position = 0;
        return new RemoteStreamContent(stream, $"{table.Report}_{input.From:yyyyMMdd}_{input.To:yyyyMMdd}.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    // ------------------------------------------------------------------ scope

    private async Task ApplyScopeAsync(ReportCode report, ReportInput input)
    {
        if (await _currentScope.IsRestaurantStaffAsync())
        {
            if (!RestaurantReports.Contains(report) || !await AuthorizationService.IsGrantedAsync(SahamPermissions.Reports.Operational))
            {
                throw new AbpAuthorizationException();
            }

            input.RestaurantId = await _currentScope.GetRestaurantIdAsync();
            return;
        }

        if (CurrentUser.IsInRole(SahamRoles.Driver))
        {
            if (!DriverReports.Contains(report))
            {
                throw new AbpAuthorizationException();
            }

            input.DriverId = (await _currentScope.GetDriverAsync()).Id;
            return;
        }

        if (!(CurrentUser.IsInRole(SahamRoles.Ops) || CurrentUser.IsInRole(SahamRoles.Accountant) || CurrentUser.IsInRole(SahamRoles.Owner)))
        {
            throw new AbpAuthorizationException();
        }

        await AuthorizationService.CheckAsync(FinancialReports.Contains(report)
            ? SahamPermissions.Reports.Financial
            : report == ReportCode.CustomerBalances && !CurrentUser.IsInRole(SahamRoles.Ops)
                ? SahamPermissions.Reports.Financial
                : SahamPermissions.Reports.Operational);
    }

    /* [from 04:00, to + 1 day 04:00) in the city's local time, as UTC. */
    private async Task<(DateTime From, DateTime To)> UtcRangeAsync(DateOnly from, DateOnly to)
    {
        var endHour = await SettingProvider.GetAsync<int>(SahamSettings.Cash.BusinessDayEndHour);
        var city = await _cityRepository.FirstOrDefaultAsync(c => c.IsActive);
        // Same arithmetic as LedgerManager.BusinessDateAsync (local − endHour), so both agree on the day.
        DateTime ToUtc(DateOnly day)
        {
            var localStart = day.ToDateTime(TimeOnly.MinValue).AddHours(endHour);
            return city?.ToUtc(localStart) ?? DateTime.SpecifyKind(localStart.AddHours(-3), DateTimeKind.Utc);
        }
        return (ToUtc(from), ToUtc(to.AddDays(1)));
    }

    // ------------------------------------------------------------------ RPT-01 restaurant sales

    private async Task<ReportTableDto> RestaurantSalesAsync(ReportInput input, DateTime fromUtc, DateTime toUtc)
    {
        var orders = await AsyncExecuter.ToListAsync((await _orderRepository.GetQueryableAsync())
            .Where(o => o.Status == OrderStatus.Delivered && o.DeliveredAt >= fromUtc && o.DeliveredAt < toUtc)
            .WhereIf(input.RestaurantId.HasValue, o => o.RestaurantId == input.RestaurantId)
            .GroupBy(o => o.RestaurantId)
            .Select(g => new { RestaurantId = g.Key, Count = g.Count(), Items = g.Sum(o => o.ItemsSubtotal), Total = g.Sum(o => o.TotalAmount) }));
        var names = await RestaurantNamesAsync(orders.Select(o => o.RestaurantId));

        return Table("Restaurant sales (RPT-01)", new[] { "Restaurant", "DeliveredOrders", "ItemsSales", "OrdersValue", "AverageOrderValue" },
            orders.OrderByDescending(o => o.Count).ThenByDescending(o => o.Items).Select(o => Row(
                ("Restaurant", names.GetValueOrDefault(o.RestaurantId)),
                ("DeliveredOrders", o.Count),
                ("ItemsSales", o.Items),
                ("OrdersValue", o.Total),
                ("AverageOrderValue", o.Count == 0 ? 0 : o.Total / o.Count))),
            ("DeliveredOrders", orders.Sum(o => o.Count)), ("ItemsSales", orders.Sum(o => o.Items)));
    }

    // ------------------------------------------------------------------ RPT-02 delivery times

    private async Task<ReportTableDto> DeliveryTimesAsync(ReportInput input, DateTime fromUtc, DateTime toUtc)
    {
        var orders = await AsyncExecuter.ToListAsync((await _orderRepository.GetQueryableAsync())
            .Where(o => o.Status == OrderStatus.Delivered && o.DeliveredAt >= fromUtc && o.DeliveredAt < toUtc)
            .WhereIf(input.RestaurantId.HasValue, o => o.RestaurantId == input.RestaurantId)
            .Select(o => new { o.RestaurantId, o.OrderType, o.ScheduledFor, o.PlacedAt, o.AcceptedAt, o.ReadyAt, o.PickedUpAt, o.DeliveredAt }));
        var names = await RestaurantNamesAsync(orders.Select(o => o.RestaurantId));

        static double Minutes(DateTime? a, DateTime? b) => a.HasValue && b.HasValue ? (b.Value - a.Value).TotalMinutes : double.NaN;
        static object? Avg(IEnumerable<double> values) { var v = values.Where(x => !double.IsNaN(x)).ToList(); return v.Count == 0 ? null : Math.Round(v.Average(), 1); }
        static object? Median(IEnumerable<double> values)
        {
            var v = values.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
            return v.Count == 0 ? null : Math.Round(v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2, 1);
        }

        var rows = orders.GroupBy(o => o.RestaurantId).Select(g =>
        {
            var asap = g.Where(o => o.OrderType == Saham.Entities.Pricing.OrderType.Asap).ToList();
            var scheduled = g.Where(o => o.OrderType == Saham.Entities.Pricing.OrderType.Scheduled).ToList();
            return Row(
                ("Restaurant", names.GetValueOrDefault(g.Key)),
                ("Orders", g.Count()),
                ("AvgTotalMinutes", Avg(asap.Select(o => Minutes(o.PlacedAt, o.DeliveredAt)))),
                ("MedianTotalMinutes", Median(asap.Select(o => Minutes(o.PlacedAt, o.DeliveredAt)))),
                ("AvgAcceptWait", Avg(asap.Select(o => Minutes(o.PlacedAt, o.AcceptedAt)))),
                ("AvgPreparation", Avg(asap.Select(o => Minutes(o.AcceptedAt, o.ReadyAt)))),
                ("AvgDriverWait", Avg(asap.Select(o => Minutes(o.ReadyAt, o.PickedUpAt)))),
                ("AvgTrip", Avg(asap.Select(o => Minutes(o.PickedUpAt, o.DeliveredAt)))),
                ("ScheduledOrders", scheduled.Count),
                ("AvgScheduledDeviation", Avg(scheduled.Select(o => Minutes(o.ScheduledFor, o.DeliveredAt)))));
        });

        return Table("Delivery times in minutes (RPT-02)", new[]
        {
            "Restaurant", "Orders", "AvgTotalMinutes", "MedianTotalMinutes", "AvgAcceptWait", "AvgPreparation", "AvgDriverWait", "AvgTrip",
            "ScheduledOrders", "AvgScheduledDeviation"
        }, rows, ("AvgTotalMinutes", Avg(orders.Where(o => o.OrderType == Saham.Entities.Pricing.OrderType.Asap).Select(o => Minutes(o.PlacedAt, o.DeliveredAt)))));
    }

    // ------------------------------------------------------------------ RPT-03 driver performance

    private async Task<ReportTableDto> DriverPerformanceAsync(ReportInput input, DateTime fromUtc, DateTime toUtc)
    {
        var assignments = await AsyncExecuter.ToListAsync((await _assignmentRepository.GetQueryableAsync())
            .Where(a => a.AssignedAt >= fromUtc && a.AssignedAt < toUtc)
            .WhereIf(input.DriverId.HasValue, a => a.DriverId == input.DriverId)
            .Select(a => new { a.DriverId, a.OrderId, a.Status, a.ReleaseReason, a.GeofenceFlag, a.DeliveredAt }));
        var offers = await AsyncExecuter.ToListAsync((await _offerRepository.GetQueryableAsync())
            .Where(o => o.OfferedAt >= fromUtc && o.OfferedAt < toUtc)
            .WhereIf(input.DriverId.HasValue, o => o.DriverId == input.DriverId)
            .Select(o => new { o.DriverId, o.Status }));
        var orderIds = assignments.Where(a => a.Status == AssignmentStatus.Delivered).Select(a => a.OrderId).ToList();
        var orders = (await AsyncExecuter.ToListAsync((await _orderRepository.GetQueryableAsync())
            .Where(o => orderIds.Contains(o.Id))
            .Select(o => new { o.Id, o.EtaMaxAt, o.DeliveredAt, o.ProofMethod }))).ToDictionary(o => o.Id);
        var cash = await AsyncExecuter.ToListAsync((await _cashRepository.GetQueryableAsync())
            .Where(c => c.MovementType == CashMovementType.Handover && c.CreationTime >= fromUtc && c.CreationTime < toUtc)
            .WhereIf(input.DriverId.HasValue, c => c.DriverId == input.DriverId)
            .Select(c => new { c.DriverId, c.Amount, c.ExpectedAmount }));

        var driverIds = assignments.Select(a => a.DriverId).Concat(offers.Select(o => o.DriverId)).Distinct().ToList();
        var names = await UserNamesAsync(driverIds);
        var rows = driverIds.Select(d =>
        {
            var delivered = assignments.Where(a => a.DriverId == d && a.Status == AssignmentStatus.Delivered).ToList();
            var onTime = delivered.Count(a => orders.TryGetValue(a.OrderId, out var o) && o.DeliveredAt <= o.EtaMaxAt);
            var driverOffers = offers.Where(o => o.DriverId == d).ToList();
            var missed = driverOffers.Count(o => o.Status is OfferStatus.Declined or OfferStatus.Expired);
            return Row(
                ("Driver", names.GetValueOrDefault(d)),
                ("Deliveries", delivered.Count),
                ("OnTimePercent", delivered.Count == 0 ? null : Math.Round(100.0 * onTime / delivered.Count, 1)),
                ("Offers", driverOffers.Count),
                ("DeclinedOrMissedPercent", driverOffers.Count == 0 ? null : Math.Round(100.0 * missed / driverOffers.Count, 1)),
                ("GaveUp", assignments.Count(a => a.DriverId == d && a.ReleaseReason != null && a.ReleaseReason.StartsWith("DRIVER_GAVE_UP"))),
                ("ManualConfirmations", delivered.Count(a => orders.TryGetValue(a.OrderId, out var o) && o.ProofMethod == DeliveryProofMethod.OpsOverride)),
                ("FarFromAddressFlags", delivered.Count(a => a.GeofenceFlag)),
                ("CashDifferences", cash.Where(c => c.DriverId == d).Sum(c => (c.ExpectedAmount ?? c.Amount) - c.Amount)));
        }).OrderByDescending(r => r["Deliveries"]);

        return Table("Driver performance (RPT-03)", new[]
        {
            "Driver", "Deliveries", "OnTimePercent", "Offers", "DeclinedOrMissedPercent", "GaveUp", "ManualConfirmations",
            "FarFromAddressFlags", "CashDifferences"
        }, rows);
    }

    // ------------------------------------------------------------------ RPT-04 monthly profit

    /* Three levels that are never added together: order value; revenue (commission + delivery fee);
     * contribution = revenue − driver pay − discounts − adjustments − wallet fees; net profit = contribution
     * − approved operating expenses, shown as incomplete when no expense was posted for the period. */
    private async Task<ReportTableDto> MonthlyProfitAsync(ReportInput input, DateTime fromUtc, DateTime toUtc)
    {
        var delivered = await AsyncExecuter.ToListAsync((await _orderRepository.GetQueryableAsync())
            .Where(o => o.Status == OrderStatus.Delivered && o.DeliveredAt >= fromUtc && o.DeliveredAt < toUtc)
            .WhereIf(input.RestaurantId.HasValue, o => o.RestaurantId == input.RestaurantId)
            .Select(o => new { o.TotalAmount, o.ItemsSubtotal }));

        var lines = await _lineRepository.GetQueryableAsync();
        var entries = await _entryRepository.GetQueryableAsync();
        var accounts = await _accountRepository.GetQueryableAsync();
        var codes = new[]
        {
            LedgerAccountCode.CommissionRevenue, LedgerAccountCode.DeliveryFeeRevenue, LedgerAccountCode.DriverPayExpense,
            LedgerAccountCode.DiscountExpense, LedgerAccountCode.AdjustmentExpense, LedgerAccountCode.WalletFeeExpense,
            LedgerAccountCode.OperatingExpense
        };
        var balances = (await AsyncExecuter.ToListAsync(
                from l in lines
                join e in entries on l.EntryId equals e.Id
                join a in accounts on l.AccountId equals a.Id
                where codes.Contains(a.Code) && e.BusinessDate >= input.From && e.BusinessDate <= input.To
                group l by a.Code into g
                select new { Code = g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) }))
            .ToDictionary(x => x.Code, x => x.Debit - x.Credit);
        long Bal(LedgerAccountCode c) => balances.GetValueOrDefault(c);

        var revenue = -(Bal(LedgerAccountCode.CommissionRevenue) + Bal(LedgerAccountCode.DeliveryFeeRevenue));
        var contribution = revenue - Bal(LedgerAccountCode.DriverPayExpense) - Bal(LedgerAccountCode.DiscountExpense)
                           - Bal(LedgerAccountCode.AdjustmentExpense) - Bal(LedgerAccountCode.WalletFeeExpense);
        var expenses = Bal(LedgerAccountCode.OperatingExpense);
        var incomplete = !balances.ContainsKey(LedgerAccountCode.OperatingExpense);

        var rows = new List<Dictionary<string, object?>>
        {
            Row(("Line", "OrdersValue (Σ T)"), ("Amount", delivered.Sum(o => o.TotalAmount))),
            Row(("Line", "RestaurantSales (Σ S)"), ("Amount", delivered.Sum(o => o.ItemsSubtotal))),
            Row(("Line", "CommissionRevenue"), ("Amount", -Bal(LedgerAccountCode.CommissionRevenue))),
            Row(("Line", "DeliveryFeeRevenue"), ("Amount", -Bal(LedgerAccountCode.DeliveryFeeRevenue))),
            Row(("Line", "Revenue"), ("Amount", revenue)),
            Row(("Line", "DriverPayExpense"), ("Amount", Bal(LedgerAccountCode.DriverPayExpense))),
            Row(("Line", "DiscountExpense"), ("Amount", Bal(LedgerAccountCode.DiscountExpense))),
            Row(("Line", "AdjustmentExpense"), ("Amount", Bal(LedgerAccountCode.AdjustmentExpense))),
            Row(("Line", "WalletFeeExpense"), ("Amount", Bal(LedgerAccountCode.WalletFeeExpense))),
            Row(("Line", "Contribution"), ("Amount", contribution)),
            Row(("Line", "OperatingExpense"), ("Amount", expenses)),
            Row(("Line", "NetProfit"), ("Amount", contribution - expenses))
        };

        return Table("Monthly profit (RPT-04)", new[] { "Line", "Amount" }, rows,
            ("DeliveredOrders", delivered.Count), ("NetProfitIncomplete", incomplete));
    }

    // ------------------------------------------------------------------ RPT-07 order counts

    private async Task<ReportTableDto> OrderCountsAsync(ReportInput input, DateTime fromUtc, DateTime toUtc)
    {
        var orders = await AsyncExecuter.ToListAsync((await _orderRepository.GetQueryableAsync())
            .Where(o => o.CreationTime >= fromUtc && o.CreationTime < toUtc)
            .WhereIf(input.RestaurantId.HasValue, o => o.RestaurantId == input.RestaurantId)
            .GroupBy(o => new { o.Status, o.PaymentMethod, o.OrderType })
            .Select(g => new { g.Key.Status, g.Key.PaymentMethod, g.Key.OrderType, Count = g.Count() }));

        return Table("Orders by final state (RPT-07)", new[] { "Status", "PaymentMethod", "OrderType", "Orders" },
            orders.OrderBy(o => o.Status).Select(o => Row(
                ("Status", o.Status.ToString()), ("PaymentMethod", o.PaymentMethod.ToString()),
                ("OrderType", o.OrderType.ToString()), ("Orders", o.Count))),
            ("Orders", orders.Sum(o => o.Count)), ("Delivered", orders.Where(o => o.Status == OrderStatus.Delivered).Sum(o => o.Count)));
    }

    // ------------------------------------------------------------------ RPT-08 cancellations

    private async Task<ReportTableDto> CancellationsAsync(ReportInput input, DateTime fromUtc, DateTime toUtc)
    {
        var cancelled = await AsyncExecuter.ToListAsync((await _orderRepository.GetQueryableAsync())
            .Where(o => o.Status == OrderStatus.Cancelled && o.CancelledAt >= fromUtc && o.CancelledAt < toUtc)
            .WhereIf(input.RestaurantId.HasValue, o => o.RestaurantId == input.RestaurantId)
            .Select(o => new { o.RestaurantId, o.CancelReason, o.CancelledByType, o.CancelChargedTo, AfterAcceptance = o.AcceptedAt != null }));
        var total = await AsyncExecuter.CountAsync((await _orderRepository.GetQueryableAsync())
            .Where(o => o.CreationTime >= fromUtc && o.CreationTime < toUtc)
            .WhereIf(input.RestaurantId.HasValue, o => o.RestaurantId == input.RestaurantId));
        var names = await RestaurantNamesAsync(cancelled.Select(o => o.RestaurantId));

        var rows = cancelled
            .GroupBy(o => new { o.RestaurantId, o.CancelReason, o.CancelledByType, o.CancelChargedTo, o.AfterAcceptance })
            .Select(g => Row(
                ("Restaurant", names.GetValueOrDefault(g.Key.RestaurantId)),
                ("Reason", g.Key.CancelReason?.ToString()),
                ("CancelledBy", g.Key.CancelledByType?.ToString()),
                ("ChargedTo", g.Key.CancelChargedTo?.ToString()),
                ("AfterAcceptance", g.Key.AfterAcceptance),
                ("Orders", g.Count()),
                ("PercentOfOrders", total == 0 ? 0 : Math.Round(100.0 * g.Count() / total, 1))))
            .OrderByDescending(r => r["Orders"]);

        return Table("Cancellations (RPT-08)", new[] { "Restaurant", "Reason", "CancelledBy", "ChargedTo", "AfterAcceptance", "Orders", "PercentOfOrders" },
            rows, ("Cancelled", cancelled.Count), ("CancelledAfterAcceptance", cancelled.Count(o => o.AfterAcceptance)), ("AllOrders", total));
    }

    // ------------------------------------------------------------------ RPT-09 settlements

    private async Task<ReportTableDto> SettlementsAsync(ReportInput input)
    {
        var settlements = await AsyncExecuter.ToListAsync((await _settlementRepository.GetQueryableAsync())
            .Where(s => s.PeriodStart >= input.From && s.PeriodStart <= input.To)
            .WhereIf(input.RestaurantId.HasValue, s => s.PartyId == input.RestaurantId)
            .WhereIf(input.DriverId.HasValue, s => s.PartyId == input.DriverId)
            .OrderBy(s => s.PeriodStart));
        var ids = settlements.Select(s => s.PartyId).ToList();
        var names = (await RestaurantNamesAsync(ids)).Concat(await UserNamesAsync(ids)).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.First().Value);

        return Table("Statements (RPT-09)", new[]
            {
                "Type", "Party", "PeriodStart", "PeriodEnd", "Status", "Orders", "Debits", "Credits", "Net", "PaidAt", "Reference"
            },
            settlements.Select(s => Row(
                ("Type", s.SettlementType.ToString()), ("Party", names.GetValueOrDefault(s.PartyId)),
                ("PeriodStart", s.PeriodStart.ToString("yyyy-MM-dd")), ("PeriodEnd", s.PeriodEnd.ToString("yyyy-MM-dd")),
                ("Status", s.Status.ToString()), ("Orders", s.OrdersCount), ("Debits", s.TotalDebits), ("Credits", s.TotalCredits),
                ("Net", s.NetAmount), ("PaidAt", s.PaidAt?.ToString("yyyy-MM-dd HH:mm")), ("Reference", s.PaidReference))),
            ("Paid", settlements.Where(s => s.Status == SettlementStatus.Paid).Sum(s => s.NetAmount)),
            ("Pending", settlements.Where(s => s.Status is SettlementStatus.Draft or SettlementStatus.Confirmed).Sum(s => s.NetAmount)));
    }

    // ------------------------------------------------------------------ RPT-11 refunds

    private async Task<ReportTableDto> RefundsAsync(ReportInput input, DateTime fromUtc, DateTime toUtc)
    {
        var refunds = await AsyncExecuter.ToListAsync((await _refundRepository.GetQueryableAsync())
            .Where(r => r.CreationTime >= fromUtc && r.CreationTime < toUtc)
            .Select(r => new { r.Status, r.Reason, r.Amount, r.CreationTime, r.CompletedAt }));

        var rows = refunds.GroupBy(r => new { r.Status, r.Reason }).Select(g => Row(
            ("Status", g.Key.Status.ToString()),
            ("Reason", g.Key.Reason.ToString()),
            ("Count", g.Count()),
            ("Amount", g.Sum(r => r.Amount)),
            ("AvgMinutesToComplete", g.Any(r => r.CompletedAt.HasValue)
                ? Math.Round(g.Where(r => r.CompletedAt.HasValue).Average(r => (r.CompletedAt!.Value - r.CreationTime).TotalMinutes), 1)
                : null)));

        return Table("Refunds (RPT-11)", new[] { "Status", "Reason", "Count", "Amount", "AvgMinutesToComplete" }, rows,
            ("PendingManualReview", refunds.Count(r => r.Status == RefundStatus.ManualReview)),
            ("RefundedAmount", refunds.Where(r => r.Status is RefundStatus.Succeeded or RefundStatus.ResolvedManually).Sum(r => r.Amount)));
    }

    // ------------------------------------------------------------------ RPT-12 customer balances

    private async Task<ReportTableDto> CustomerBalancesAsync()
    {
        var lines = await _lineRepository.GetQueryableAsync();
        var accounts = await _accountRepository.GetQueryableAsync();
        var balances = await AsyncExecuter.ToListAsync(
            from l in lines
            join a in accounts on l.AccountId equals a.Id
            where a.Code == LedgerAccountCode.CustomerBalance && a.PartyId != null
            group l by a.PartyId into g
            select new { CustomerId = g.Key!.Value, Balance = g.Sum(x => x.Credit - x.Debit) });
        var profiles = (await _profileRepository.GetListAsync()).ToDictionary(p => p.Id);
        var ids = balances.Where(b => b.Balance != 0).Select(b => b.CustomerId).Union(profiles.Values.Where(p => p.NoShowCount > 0).Select(p => p.Id)).ToList();
        var users = (await _userRepository.GetListAsync(u => ids.Contains(u.Id))).ToDictionary(u => u.Id);
        var byCustomer = balances.ToDictionary(b => b.CustomerId, b => b.Balance);

        var rows = ids.Select(id =>
        {
            var balance = byCustomer.GetValueOrDefault(id);
            var profile = profiles.GetValueOrDefault(id);
            return Row(
                ("Customer", users.GetValueOrDefault(id)?.Name),
                ("Phone", users.GetValueOrDefault(id)?.PhoneNumber),
                ("Credit", balance > 0 ? balance : 0),
                ("Debt", balance < 0 ? -balance : 0),
                ("NoShows", profile?.NoShowCount ?? 0),
                ("CashDisabled", profile?.CashDisabled ?? false));
        }).OrderByDescending(r => r["Debt"]);

        return Table("Customer balances and debts (RPT-12)", new[] { "Customer", "Phone", "Credit", "Debt", "NoShows", "CashDisabled" }, rows,
            ("UncollectedDebt", balances.Where(b => b.Balance < 0).Sum(b => -b.Balance)),
            ("CustomerCredit", balances.Where(b => b.Balance > 0).Sum(b => b.Balance)));
    }

    // ------------------------------------------------------------------ RPT-13 driver weekly earnings

    private async Task<ReportTableDto> DriverWeeklyAsync(ReportInput input)
    {
        var lines = await _lineRepository.GetQueryableAsync();
        var entries = await _entryRepository.GetQueryableAsync();
        var accounts = await _accountRepository.GetQueryableAsync();
        var movements = await AsyncExecuter.ToListAsync(
            from l in lines
            join e in entries on l.EntryId equals e.Id
            join a in accounts on l.AccountId equals a.Id
            where a.Code == LedgerAccountCode.DriverPayable && a.PartyId != null &&
                  e.BusinessDate >= input.From && e.BusinessDate <= input.To &&
                  (!input.DriverId.HasValue || a.PartyId == input.DriverId)
            select new { DriverId = a.PartyId!.Value, e.EntryType, l.Debit, l.Credit, l.OrderId });
        var driverIds = movements.Select(m => m.DriverId).Distinct().ToList();
        var outstanding = await AsyncExecuter.ToListAsync(
            from l in lines
            join a in accounts on l.AccountId equals a.Id
            where a.Code == LedgerAccountCode.DriverPayable && a.PartyId != null && driverIds.Contains(a.PartyId.Value)
            group l by a.PartyId into g
            select new { DriverId = g.Key!.Value, Balance = g.Sum(x => x.Credit - x.Debit) });
        var names = await UserNamesAsync(driverIds);

        var rows = driverIds.Select(d =>
        {
            var mine = movements.Where(m => m.DriverId == d).ToList();
            return Row(
                ("Driver", names.GetValueOrDefault(d)),
                ("Orders", mine.Where(m => m.EntryType == JournalEntryType.DriverPay).Select(m => m.OrderId).Distinct().Count()),
                ("Pay", mine.Where(m => m.EntryType == JournalEntryType.DriverPay).Sum(m => m.Credit)),
                ("CancellationPay", mine.Where(m => m.EntryType == JournalEntryType.CancelDriverPay).Sum(m => m.Credit)),
                ("Paid", mine.Where(m => m.EntryType == JournalEntryType.DriverPayout).Sum(m => m.Debit)),
                ("Outstanding", outstanding.FirstOrDefault(o => o.DriverId == d)?.Balance ?? 0));
        }).OrderByDescending(r => r["Pay"]);

        return Table("Driver earnings (RPT-13)", new[] { "Driver", "Orders", "Pay", "CancellationPay", "Paid", "Outstanding" }, rows);
    }

    // ------------------------------------------------------------------ helpers

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells) =>
        cells.ToDictionary(c => c.Key, c => c.Value);

    private static ReportTableDto Table(string title, IEnumerable<string> columns, IEnumerable<Dictionary<string, object?>> rows,
        params (string Key, object? Value)[] summary) => new()
    {
        Title = title,
        Columns = columns.ToList(),
        Rows = rows.ToList(),
        Summary = summary.ToDictionary(s => s.Key, s => s.Value)
    };

    private async Task<Dictionary<Guid, string>> RestaurantNamesAsync(IEnumerable<Guid> ids)
    {
        var list = ids.Distinct().ToList();
        return (await _restaurantRepository.GetListAsync(r => list.Contains(r.Id), includeDetails: false)).ToDictionary(r => r.Id, r => r.Name.Ar);
    }

    private async Task<Dictionary<Guid, string>> UserNamesAsync(IEnumerable<Guid> ids)
    {
        var list = ids.Distinct().ToList();
        return (await _userRepository.GetListAsync(u => list.Contains(u.Id))).ToDictionary(u => u.Id, u => u.Name ?? u.UserName);
    }
}
