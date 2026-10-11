using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Saham.Entities.Support;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus;
using Volo.Abp.Uow;

namespace Saham.Hubs;

/* Tells the ops board (and its alert sound) that an incident was opened, taken or closed,
 * after commit — the board re-reads the queue through the API (DER-NTF-02). */
public class IncidentRealtimeNotifier : ILocalEventHandler<IncidentChangedEto>, ITransientDependency
{
    public const string MethodName = "incidentChanged";
    private const string PendingKey = "Saham.IncidentRealtimeNotifier.Pending";

    private readonly IHubContext<OrdersHub> _hubContext;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<IncidentRealtimeNotifier> _logger;

    public IncidentRealtimeNotifier(IHubContext<OrdersHub> hubContext, IUnitOfWorkManager unitOfWorkManager, ILogger<IncidentRealtimeNotifier> logger)
    {
        _hubContext = hubContext;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
    }

    public Task HandleEventAsync(IncidentChangedEto eventData)
    {
        var uow = _unitOfWorkManager.Current;
        if (uow == null)
        {
            return SendAsync(eventData);
        }

        // The latest state of each incident, sent once when the unit of work completes.
        var pending = (Dictionary<System.Guid, IncidentChangedEto>)uow.Items.GetOrAdd(PendingKey, () =>
        {
            var map = new Dictionary<System.Guid, IncidentChangedEto>();
            uow.OnCompleted(async () =>
            {
                foreach (var e in map.Values)
                {
                    await SendAsync(e);
                }
            });
            return map;
        });
        pending[eventData.IncidentId] = eventData;
        return Task.CompletedTask;
    }

    private async Task SendAsync(IncidentChangedEto e)
    {
        var payload = new
        {
            incidentId = e.IncidentId,
            incidentType = e.IncidentType.ToString(),
            status = e.Status.ToString(),
            severity = e.Severity.ToString(),
            orderId = e.OrderId
        };

        try
        {
            await _hubContext.Clients.Group(OrdersHub.OpsGroup).SendAsync(MethodName, payload);
            if (System.Array.IndexOf(Incident.FinancialTypes, e.IncidentType) >= 0)
            {
                await _hubContext.Clients.Group(OrdersHub.FinanceGroup).SendAsync(MethodName, payload);
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning(ex, "Realtime notification failed for incident {IncidentId}", e.IncidentId);
        }
    }
}
