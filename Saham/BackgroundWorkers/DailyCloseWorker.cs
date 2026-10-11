using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Saham.Entities.Ledger;
using Saham.Entities.Settlements;
using Saham.Settings;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Settings;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Saham.BackgroundWorkers;

/* The operating-day close (ASM-09: the day ends at 04:00). Every few minutes it makes sure the
 * statements of the periods that just closed exist — generating is idempotent, so a missed run or a
 * restart simply catches up on the next pass:
 *   every day       → DRIVER_DAILY of yesterday (UC-23 step 4);
 *   on Thursday     → DRIVER_WEEKLY_PAYOUT up to Wednesday (BR-068);
 *   on Saturday     → RESTAURANT_WEEKLY of Saturday → Friday (BR-040).
 * The accountant then confirms them and records the payments (SettlementAppService). */
public class DailyCloseWorker : AsyncPeriodicBackgroundWorkerBase
{
    public DailyCloseWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromMinutes(10).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var services = workerContext.ServiceProvider;
        var settings = services.GetRequiredService<ISettingProvider>();
        var today = await services.GetRequiredService<LedgerManager>().BusinessDateAsync(services.GetRequiredService<IClock>().Now);

        await GenerateAsync(services, SettlementType.DriverDaily, today.AddDays(-1), today);

        if (today.DayOfWeek == Enum.Parse<DayOfWeek>((await settings.GetOrNullAsync(SahamSettings.Settlements.DriverPayoutDay))!))
        {
            await GenerateAsync(services, SettlementType.DriverWeeklyPayout, today.AddDays(-7), today);
        }

        if (today.DayOfWeek == Enum.Parse<DayOfWeek>((await settings.GetOrNullAsync(SahamSettings.Settlements.RestaurantWeekStartDay))!))
        {
            await GenerateAsync(services, SettlementType.RestaurantWeekly, today.AddDays(-7), today);
        }
    }

    private async Task GenerateAsync(IServiceProvider services, SettlementType type, DateOnly periodStart, DateOnly today)
    {
        try
        {
            using var uow = services.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
            var created = await services.GetRequiredService<SettlementManager>().GenerateAsync(type, periodStart, today);
            await uow.CompleteAsync();
            if (created.Count > 0)
            {
                Logger.LogInformation("Generated {Count} {Type} statements for {PeriodStart}", created.Count, type, periodStart);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Statement close failed for {Type} {PeriodStart}", type, periodStart);
        }
    }
}
