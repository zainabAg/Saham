using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Saham.Entities.Drivers;
using Saham.Entities.Geography;
using Saham.Entities.Ledger;
using Saham.Settings;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Settings;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Saham.BackgroundWorkers;

/* BR-066 (DEC-09): from 12:00 local time, a driver still holding cash from an earlier operating
 * day is blocked from cash orders and the owner / accountant are alerted (CASH_OVERDUE incident).
 * Wallet orders keep coming. The block lifts by itself when the cash is handed over (CashManager). */
public class CashOverdueWorker : AsyncPeriodicBackgroundWorkerBase
{
    public CashOverdueWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromMinutes(5).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var services = workerContext.ServiceProvider;
        var settings = services.GetRequiredService<ISettingProvider>();
        var now = services.GetRequiredService<IClock>().Now;
        var unitOfWorkManager = services.GetRequiredService<IUnitOfWorkManager>();

        using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);

        var city = await services.GetRequiredService<IRepository<City, Guid>>().FirstOrDefaultAsync(c => c.IsActive);
        var overdueHour = await settings.GetAsync<int>(SahamSettings.Cash.OverdueHour);
        if (city == null || city.ToLocalTime(now).Hour < overdueHour)
        {
            return;
        }

        var ledger = services.GetRequiredService<LedgerManager>();
        var cash = services.GetRequiredService<CashManager>();
        var drivers = services.GetRequiredService<IRepository<Driver, Guid>>();
        var today = await ledger.BusinessDateAsync(now, city.Id);

        // Only drivers not already blocked as overdue and holding some cash.
        var candidates = await drivers.GetListAsync(d => d.CashBlocked == null || d.CashBlocked == DriverCashBlock.Cap);
        var balances = await ledger.PartyBalancesAsync(LedgerAccountCode.DriverCash, candidates.Select(d => d.Id).ToList());

        foreach (var driver in candidates.Where(d => balances.GetValueOrDefault(d.Id) > 0))
        {
            if (await cash.HasOverdueCashAsync(driver.Id, today))
            {
                await cash.BlockOverdueAsync(driver, today);
                Logger.LogInformation("Driver {DriverId} blocked from cash orders: cash not handed over", driver.Id);
            }
        }

        await uow.CompleteAsync();
    }
}
