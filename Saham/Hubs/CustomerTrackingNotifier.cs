using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace Saham.Hubs;

/* Pushes the driver's position to the customer of an active order (REQ-CUS-11), after commit.
 * Only from assignment to delivery: outside that window nobody is sent a position (BR-036). */
public class CustomerTrackingNotifier : ITransientDependency
{
    public const string MethodName = "driverLocation";

    private readonly IHubContext<OrdersHub> _hubContext;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<CustomerTrackingNotifier> _logger;

    public CustomerTrackingNotifier(IHubContext<OrdersHub> hubContext, IUnitOfWorkManager unitOfWorkManager, ILogger<CustomerTrackingNotifier> logger)
    {
        _hubContext = hubContext;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
    }

    public void Queue(Guid customerId, Guid orderId, double latitude, double longitude, DateTime at)
    {
        async Task Send()
        {
            try
            {
                await _hubContext.Clients.User(customerId.ToString())
                    .SendAsync(MethodName, new { orderId, latitude, longitude, at });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tracking push failed for order {OrderId}", orderId);
            }
        }

        if (_unitOfWorkManager.Current == null)
        {
            _ = Send();
        }
        else
        {
            _unitOfWorkManager.Current.OnCompleted(Send);
        }
    }
}
