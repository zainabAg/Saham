using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Saham.Entities.Dispatch;
using Saham.Entities.Drivers;
using Saham.Entities.Support;
using Saham.Settings;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Settings;
using Volo.Abp.Threading;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Saham.BackgroundWorkers;

/* EX-02 / BR-057: a driver with an order who has not sent a location for N minutes (phone off,
 * battery dead) opens a DRIVER_STALE incident — high severity when the food is already with him.
 * No automatic reassignment: the order stays with the driver and ops follow the procedure (REC-07).
 * The incident closes itself when locations resume (DriverLocationAppService) or on delivery. */
public class DriverStaleWorker : AsyncPeriodicBackgroundWorkerBase
{
    public DriverStaleWorker(AbpAsyncTimer timer, IServiceScopeFactory serviceScopeFactory)
        : base(timer, serviceScopeFactory)
    {
        Timer.Period = (int)TimeSpan.FromSeconds(30).TotalMilliseconds;
    }

    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var services = workerContext.ServiceProvider;
        var unitOfWorkManager = services.GetRequiredService<IUnitOfWorkManager>();
        var drivers = services.GetRequiredService<IRepository<Driver, Guid>>();
        var assignments = services.GetRequiredService<IRepository<DeliveryAssignment, Guid>>();
        var incidentManager = services.GetRequiredService<IncidentManager>();
        var asyncExecuter = services.GetRequiredService<IAsyncQueryableExecuter>();
        var staleMinutes = await services.GetRequiredService<ISettingProvider>().GetAsync<int>(SahamSettings.Drivers.StaleMinutes);
        var staleBefore = services.GetRequiredService<IClock>().Now.AddMinutes(-staleMinutes);

        using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);

        var active = await assignments.GetQueryableAsync();
        var stale = await asyncExecuter.ToListAsync((await drivers.GetQueryableAsync())
            .Where(d => d.ActiveOrderCount > 0 && (d.LastLocationAt == null || d.LastLocationAt < staleBefore))
            .Select(d => new
            {
                d.Id,
                Order = active.Where(a => a.DriverId == d.Id && (a.Status == AssignmentStatus.Assigned || a.Status == AssignmentStatus.PickedUp))
                    .OrderByDescending(a => a.Status)
                    .Select(a => new { a.OrderId, a.Status })
                    .FirstOrDefault()
            }));

        foreach (var driver in stale)
        {
            var foodInHand = driver.Order?.Status == AssignmentStatus.PickedUp;
            await incidentManager.OpenAsync(IncidentType.DriverStale, foodInHand ? IncidentSeverity.Critical : IncidentSeverity.High,
                driver.Id, orderId: driver.Order?.OrderId, driverId: driver.Id);
        }

        await uow.CompleteAsync();
    }
}
