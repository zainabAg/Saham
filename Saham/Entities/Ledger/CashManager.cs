using System;
using System.Linq;
using System.Threading.Tasks;
using Saham.Entities.Drivers;
using Saham.Entities.Support;
using Saham.Settings;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Settings;
using A = Saham.Entities.Ledger.LedgerAccountCode;

namespace Saham.Entities.Ledger;

/* Driver cash (UC-23, DEC-09):
 *   - every dinar collected sits in DRIVER_CASH until the accountant counts it in (J-HANDOVER);
 *     earnings are never netted against cash (J-DRIVER-NETTING was dropped by the client);
 *   - above the cap during a shift → no more cash orders until handed over (BR-067);
 *   - cash from a previous operating day not handed over by 12:00 → blocked + alert (BR-066);
 *   - the block lifts automatically when the cash is handed over. */
public class CashManager : DomainService
{
    private readonly LedgerManager _ledger;
    private readonly IRepository<CashMovement, Guid> _movementRepository;
    private readonly IRepository<Driver, Guid> _driverRepository;
    private readonly IRepository<JournalLine, Guid> _lineRepository;
    private readonly IRepository<JournalEntry, Guid> _entryRepository;
    private readonly IRepository<LedgerAccount, Guid> _accountRepository;
    private readonly IncidentManager _incidentManager;
    private readonly ISettingProvider _settingProvider;

    public CashManager(
        LedgerManager ledger,
        IRepository<CashMovement, Guid> movementRepository,
        IRepository<Driver, Guid> driverRepository,
        IRepository<JournalLine, Guid> lineRepository,
        IRepository<JournalEntry, Guid> entryRepository,
        IRepository<LedgerAccount, Guid> accountRepository,
        IncidentManager incidentManager,
        ISettingProvider settingProvider)
    {
        _ledger = ledger;
        _movementRepository = movementRepository;
        _driverRepository = driverRepository;
        _lineRepository = lineRepository;
        _entryRepository = entryRepository;
        _accountRepository = accountRepository;
        _incidentManager = incidentManager;
        _settingProvider = settingProvider;
    }

    public Task<long> CashInHandAsync(Guid driverId) => _ledger.PartyBalanceAsync(A.DriverCash, driverId);

    /* UC-23: the accountant records what he counted. The expected amount is the balance at that
     * moment; a difference stays on the driver (EX-20). */
    public async Task<CashMovement> RecordHandoverAsync(Driver driver, long countedAmount, string? note, Guid accountantId)
    {
        if (countedAmount <= 0)
        {
            throw new BusinessException(SahamErrorCodes.InvalidCashAmount);
        }

        var expected = await CashInHandAsync(driver.Id);
        var movementId = GuidGenerator.Create();
        var entry = await _ledger.PostAsync(JournalEntryType.Handover, JournalSourceType.CashMovement, movementId,
            $"handover:{movementId:N}", "Driver cash handover", await _ledger.BusinessDateAsync(Clock.Now, driver.CityId), new[]
            {
                (await _ledger.CompanyAccountAsync(A.CashOnHand), countedAmount, 0L, (Guid?)null),
                (await _ledger.PartyAccountAsync(A.DriverCash, driver.Id), 0L, countedAmount, null)
            }, accountantId);

        var movement = await _movementRepository.InsertAsync(new CashMovement(movementId, driver.Id, CashMovementType.Handover,
            countedAmount, expected, entry.BusinessDate, accountantId, note, entry.Id));

        await RefreshBlockAsync(driver);
        return movement;
    }

    /* REC-04: change float given to / taken back from a driver (J-FLOAT-ISSUE / RETURN). */
    public async Task<CashMovement> MoveFloatAsync(Driver driver, long amount, bool issue, string? note, Guid accountantId)
    {
        if (amount <= 0)
        {
            throw new BusinessException(SahamErrorCodes.InvalidCashAmount);
        }

        if (!issue && amount > await _ledger.PartyBalanceAsync(A.DriverFloat, driver.Id))
        {
            throw new BusinessException(SahamErrorCodes.FloatReturnTooLarge);
        }

        var movementId = GuidGenerator.Create();
        var cash = await _ledger.CompanyAccountAsync(A.CashOnHand);
        var driverFloat = await _ledger.PartyAccountAsync(A.DriverFloat, driver.Id);
        var entry = await _ledger.PostAsync(issue ? JournalEntryType.FloatIssue : JournalEntryType.FloatReturn,
            JournalSourceType.CashMovement, movementId, $"float:{movementId:N}", issue ? "Change float issued" : "Change float returned",
            await _ledger.BusinessDateAsync(Clock.Now, driver.CityId), issue
                ? new[] { (driverFloat, amount, 0L, (Guid?)null), (cash, 0L, amount, (Guid?)null) }
                : new[] { (cash, amount, 0L, (Guid?)null), (driverFloat, 0L, amount, (Guid?)null) },
            accountantId);

        return await _movementRepository.InsertAsync(new CashMovement(movementId, driver.Id,
            issue ? CashMovementType.FloatIssue : CashMovementType.FloatReturn, amount, null, entry.BusinessDate, accountantId, note, entry.Id));
    }

    /* BR-067: checked after every cash collection. */
    public async Task CheckCapAsync(Driver driver)
    {
        var cap = await _settingProvider.GetAsync<long>(SahamSettings.Cash.DriverCap);
        if (driver.CashBlocked == null && await CashInHandAsync(driver.Id) > cap)
        {
            driver.BlockCash(DriverCashBlock.Cap, Clock.Now);
            await _driverRepository.UpdateAsync(driver);
            await _incidentManager.OpenAsync(IncidentType.CashCapReached, IncidentSeverity.Medium, driver.Id, driverId: driver.Id);
        }
    }

    /* BR-066: cash collected on an earlier operating day that has not been handed over yet. */
    public async Task<bool> HasOverdueCashAsync(Guid driverId, DateOnly today)
    {
        var account = await _accountRepository.FindAsync(a => a.Code == A.DriverCash && a.PartyId == driverId);
        if (account == null)
        {
            return false;
        }

        var lines = await _lineRepository.GetQueryableAsync();
        var entries = await _entryRepository.GetQueryableAsync();
        var collectedBefore = await AsyncExecuter.SumAsync(
            from l in lines join e in entries on l.EntryId equals e.Id
            where l.AccountId == account.Id && e.BusinessDate < today
            select l.Debit);
        var handedOver = await AsyncExecuter.SumAsync(lines.Where(l => l.AccountId == account.Id), l => l.Credit);

        return collectedBefore - handedOver > 0;
    }

    public async Task BlockOverdueAsync(Driver driver, DateOnly today)
    {
        driver.BlockCash(DriverCashBlock.Overdue, Clock.Now);
        await _driverRepository.UpdateAsync(driver);
        // One alert per driver per day for the owner and the accountant (BR-066).
        await _incidentManager.OpenAsync(IncidentType.CashOverdue, IncidentSeverity.High,
            DeterministicId(driver.Id, today), driverId: driver.Id);
    }

    /* After a handover: lift what no longer applies. */
    private async Task RefreshBlockAsync(Driver driver)
    {
        if (driver.CashBlocked == null)
        {
            return;
        }

        var balance = await CashInHandAsync(driver.Id);
        var cap = await _settingProvider.GetAsync<long>(SahamSettings.Cash.DriverCap);
        var today = await _ledger.BusinessDateAsync(Clock.Now, driver.CityId);

        var stillBlocked = driver.CashBlocked == DriverCashBlock.Overdue
            ? await HasOverdueCashAsync(driver.Id, today)
            : balance > cap;

        if (!stillBlocked)
        {
            driver.UnblockCash();
            await _driverRepository.UpdateAsync(driver);
            await _incidentManager.AutoResolveAsync(IncidentType.CashCapReached, driver.Id, "HANDED_OVER");
            await _incidentManager.AutoResolveAsync(IncidentType.CashOverdue, DeterministicId(driver.Id, today), "HANDED_OVER");
        }
    }

    private static Guid DeterministicId(Guid driverId, DateOnly day)
    {
        var bytes = driverId.ToByteArray();
        BitConverter.GetBytes(day.DayNumber).CopyTo(bytes, 0);
        return new Guid(bytes);
    }
}
