using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Saham.Entities.Dispatch;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus;
using Volo.Abp.Uow;

namespace Saham.Hubs;

/* Tells a driver about a new / cancelled offer and an assigned / released task, after commit.
 * Drivers are connected to OrdersHub and addressed by their user id. Push notifications for
 * a closed app go through the outbox (DriverOutboxEventHandler). */
public class DriverRealtimeNotifier : ILocalEventHandler<DriverNotificationEto>, ITransientDependency
{
    public const string MethodName = "driverEvent";

    private readonly IHubContext<OrdersHub> _hubContext;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<DriverRealtimeNotifier> _logger;

    public DriverRealtimeNotifier(IHubContext<OrdersHub> hubContext, IUnitOfWorkManager unitOfWorkManager, ILogger<DriverRealtimeNotifier> logger)
    {
        _hubContext = hubContext;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
    }

    public Task HandleEventAsync(DriverNotificationEto e)
    {
        var uow = _unitOfWorkManager.Current;
        if (uow == null)
        {
            return SendAsync(e);
        }

        uow.OnCompleted(() => SendAsync(e));
        return Task.CompletedTask;
    }

    private async Task SendAsync(DriverNotificationEto e)
    {
        try
        {
            await _hubContext.Clients.User(e.DriverId.ToString()).SendAsync(MethodName, new
            {
                kind = e.Kind.ToString(),
                orderId = e.OrderId,
                offerId = e.OfferId,
                expiresAt = e.ExpiresAt
            });
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning(ex, "Driver notification failed for driver {DriverId}", e.DriverId);
        }
    }
}
