using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Saham.Entities.Ledger;
using Saham.Entities.Notifications;
using Saham.Entities.Payments;
using Saham.Settings;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Settings;
using A = Saham.Entities.Ledger.LedgerAccountCode;

namespace Saham.Entities.Settlements;

/* Statements (BR-039/040/068, UC-23/24, DEC-09):
 *   DRIVER_DAILY        — the driver's cash account for one closed operating day (collected,
 *                         handed over, the difference carried);
 *   DRIVER_WEEKLY_PAYOUT— the driver's earnings account up to Wednesday, paid on Thursday
 *                         (J-DRIVER-PAYOUT); earnings are never netted against cash (DEC-09);
 *   RESTAURANT_WEEKLY   — the restaurant's account for Saturday → Friday: positive net = we pay
 *                         (J-RESTAURANT-PAYOUT), negative = the restaurant pays us (J-RESTAURANT-COLLECT).
 * A statement takes every not-yet-settled line of the party's account up to the period end, so
 * nothing is ever counted twice or lost; the payout line joins the same statement. */
public class SettlementManager : DomainService
{
    private readonly IRepository<Settlement, Guid> _settlementRepository;
    private readonly IRepository<JournalLine, Guid> _lineRepository;
    private readonly IRepository<JournalEntry, Guid> _entryRepository;
    private readonly IRepository<LedgerAccount, Guid> _accountRepository;
    private readonly LedgerManager _ledger;
    private readonly OutboxManager _outbox;
    private readonly ISettingProvider _settingProvider;

    public SettlementManager(
        IRepository<Settlement, Guid> settlementRepository,
        IRepository<JournalLine, Guid> lineRepository,
        IRepository<JournalEntry, Guid> entryRepository,
        IRepository<LedgerAccount, Guid> accountRepository,
        LedgerManager ledger,
        OutboxManager outbox,
        ISettingProvider settingProvider)
    {
        _settlementRepository = settlementRepository;
        _lineRepository = lineRepository;
        _entryRepository = entryRepository;
        _accountRepository = accountRepository;
        _ledger = ledger;
        _outbox = outbox;
        _settingProvider = settingProvider;
    }

    public static A AccountCodeOf(SettlementType type) => type switch
    {
        SettlementType.DriverDaily => A.DriverCash,
        SettlementType.DriverWeeklyPayout => A.DriverPayable,
        _ => A.RestaurantPayable
    };

    public static LedgerPartyType PartyTypeOf(SettlementType type) =>
        type == SettlementType.RestaurantWeekly ? LedgerPartyType.Restaurant : LedgerPartyType.Driver;

    /* The period that starts on periodStart, checked against the configured week days. */
    public async Task<DateOnly> PeriodEndAsync(SettlementType type, DateOnly periodStart)
    {
        if (type == SettlementType.DriverDaily)
        {
            return periodStart;
        }

        var startDay = Enum.Parse<DayOfWeek>((await _settingProvider.GetOrNullAsync(type == SettlementType.RestaurantWeekly
            ? SahamSettings.Settlements.RestaurantWeekStartDay
            : SahamSettings.Settlements.DriverPayoutDay))!);
        if (periodStart.DayOfWeek != startDay)
        {
            throw new BusinessException(SahamErrorCodes.InvalidSettlementPeriod).WithData("startDay", startDay.ToString());
        }

        return periodStart.AddDays(6);
    }

    /* Builds the statements of one closed period for every party with movements. Running it again
     * creates nothing new (UC-24 E1). Returns the statements created. */
    public async Task<List<Settlement>> GenerateAsync(SettlementType type, DateOnly periodStart, DateOnly today)
    {
        var periodEnd = await PeriodEndAsync(type, periodStart);
        if (periodEnd >= today)
        {
            throw new BusinessException(SahamErrorCodes.SettlementPeriodNotClosed).WithData("periodEnd", periodEnd.ToString("yyyy-MM-dd"));
        }

        var code = AccountCodeOf(type);
        var accounts = await _accountRepository.GetQueryableAsync();
        var lines = await _lineRepository.GetQueryableAsync();
        var entries = await _entryRepository.GetQueryableAsync();
        var parties = await AsyncExecuter.ToListAsync(
            (from l in lines
             join a in accounts on l.AccountId equals a.Id
             join e in entries on l.EntryId equals e.Id
             where a.Code == code && a.PartyId != null && l.SettlementId == null && e.BusinessDate <= periodEnd
             select new { AccountId = a.Id, PartyId = a.PartyId!.Value }).Distinct());

        var created = new List<Settlement>();
        foreach (var party in parties)
        {
            if (await _settlementRepository.AnyAsync(s => s.SettlementType == type && s.PartyId == party.PartyId && s.PeriodStart == periodStart))
            {
                continue;
            }

            var settlement = await _settlementRepository.InsertAsync(
                new Settlement(GuidGenerator.Create(), type, PartyTypeOf(type), party.PartyId, periodStart, periodEnd), autoSave: true);

            // The one update the ledger allows: settlement_id NULL → this statement (UC-24 step 1).
            await (await _lineRepository.GetDbSetAsync())
                .Where(l => l.AccountId == party.AccountId && l.SettlementId == null &&
                            entries.Any(e => e.Id == l.EntryId && e.BusinessDate <= periodEnd))
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.SettlementId, settlement.Id));

            var mine = lines.Where(l => l.SettlementId == settlement.Id);
            var debits = await AsyncExecuter.SumAsync(mine, l => l.Debit);
            var credits = await AsyncExecuter.SumAsync(mine, l => l.Credit);
            var orders = await AsyncExecuter.CountAsync(mine.Where(l => l.OrderId != null).Select(l => l.OrderId).Distinct());
            var opening = await AsyncExecuter.SumAsync(
                lines.Where(l => l.AccountId == party.AccountId && l.SettlementId != settlement.Id &&
                                 entries.Any(e => e.Id == l.EntryId && e.BusinessDate < periodStart)),
                l => l.Debit - l.Credit);

            settlement.SetTotals(opening, debits, credits, orders);
            await _settlementRepository.UpdateAsync(settlement, autoSave: true);
            created.Add(settlement);
        }

        return created;
    }

    /* UC-24 step 4 / BR-068: the money left the company; the payout line closes the statement's balance. */
    public async Task MarkPaidAsync(Settlement settlement, Guid accountantId, string reference, ManualPaymentChannel channel)
    {
        var partyAccount = await _ledger.PartyAccountAsync(AccountCodeOf(settlement.SettlementType), settlement.PartyId);
        var isDriver = settlement.SettlementType == SettlementType.DriverWeeklyPayout;
        var entry = await _ledger.PostAsync(
            isDriver ? JournalEntryType.DriverPayout : JournalEntryType.RestaurantPayout,
            JournalSourceType.Settlement, settlement.Id,
            $"{(isDriver ? "driver_payout" : "payout")}:{settlement.Id:N}",
            $"{settlement.SettlementType} {settlement.PeriodStart:yyyy-MM-dd} paid ({channel}) {reference}",
            await _ledger.BusinessDateAsync(Clock.Now), new[]
            {
                (partyAccount, settlement.NetAmount, 0L, (Guid?)null),
                (await CashAccountAsync(channel), 0L, settlement.NetAmount, null)
            }, accountantId);

        settlement.MarkPaid(accountantId, reference, channel, entry.Id, Clock.Now);
        await LinkAsync(entry.Id, partyAccount, settlement.Id);

        if (isDriver)
        {
            await _outbox.EnqueueAsync(OutboxEventTypes.DriverPayoutPaid, OutboxAggregateTypes.Driver, settlement.PartyId,
                new Dictionary<string, string?> { ["amount"] = settlement.NetAmount.ToString(), ["settlementId"] = settlement.Id.ToString() },
                settlement.Id.ToString("N"));
        }
    }

    /* UC-24 A1: the restaurant owed us for the week and paid (J-RESTAURANT-COLLECT). */
    public async Task MarkCollectedAsync(Settlement settlement, Guid accountantId, string reference, ManualPaymentChannel channel)
    {
        var partyAccount = await _ledger.PartyAccountAsync(AccountCodeOf(settlement.SettlementType), settlement.PartyId);
        var amount = -settlement.NetAmount;
        var entry = await _ledger.PostAsync(JournalEntryType.RestaurantCollect, JournalSourceType.Settlement, settlement.Id,
            $"collect:{settlement.Id:N}", $"Restaurant statement {settlement.PeriodStart:yyyy-MM-dd} collected ({channel}) {reference}",
            await _ledger.BusinessDateAsync(Clock.Now), new[]
            {
                (await CashAccountAsync(channel), amount, 0L, (Guid?)null),
                (partyAccount, 0L, amount, null)
            }, accountantId);

        settlement.MarkCollected(accountantId, reference, channel, entry.Id, Clock.Now);
        await LinkAsync(entry.Id, partyAccount, settlement.Id);
    }

    private async Task LinkAsync(Guid entryId, Guid accountId, Guid settlementId)
    {
        await (await _lineRepository.GetDbSetAsync())
            .Where(l => l.EntryId == entryId && l.AccountId == accountId && l.SettlementId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.SettlementId, settlementId));
    }

    private Task<Guid> CashAccountAsync(ManualPaymentChannel channel) =>
        _ledger.CompanyAccountAsync(channel == ManualPaymentChannel.Cash ? A.CashOnHand : A.CompanyBank);
}
