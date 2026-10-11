using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace Saham.Hubs;

/* In-app call events to one user over OrdersHub ("callEvent": incoming, answered, declined, ended,
 * missed), sent after commit so nobody hears about a call that was rolled back. The audio and the
 * WebRTC signalling ("callSignal") go peer to peer through OrdersHub.SendCallSignal. */
public class CallNotifier : ITransientDependency
{
    public const string EventMethod = "callEvent";
    public const string SignalMethod = "callSignal";

    private readonly IHubContext<OrdersHub> _hubContext;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<CallNotifier> _logger;

    public CallNotifier(IHubContext<OrdersHub> hubContext, IUnitOfWorkManager unitOfWorkManager, ILogger<CallNotifier> logger)
    {
        _hubContext = hubContext;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
    }

    public void Queue(Guid userId, object payload)
    {
        async Task Send()
        {
            try
            {
                await _hubContext.Clients.User(userId.ToString()).SendAsync(EventMethod, payload);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Call event to {UserId} failed", userId);
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
