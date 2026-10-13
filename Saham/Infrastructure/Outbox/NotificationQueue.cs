using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using Saham.Entities.Notifications;
using Saham.Entities.Support;
using Saham.Settings;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Settings;
using Volo.Abp.Uow;

namespace Saham.Infrastructure.Outbox;

/* Wires the Phase 6 engine to the project: OutboxOptions from settings (operational defaults, not
 * business rules), a NEW Npgsql connection per store call (never the request's), the real
 * NotificationSender, and the delivery-code incident when such an SMS ends Dead (REQ-NTF-02). */
public class NotificationQueue : ITransientDependency
{
    private readonly ISettingProvider _settingProvider;
    private readonly IConnectionStringResolver _connectionStrings;
    private readonly NotificationSender _sender;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IncidentManager _incidentManager;
    private readonly IRepository<Entities.Orders.Order, Guid> _orderRepository;

    public NotificationQueue(
        ISettingProvider settingProvider,
        IConnectionStringResolver connectionStrings,
        NotificationSender sender,
        IUnitOfWorkManager unitOfWorkManager,
        IncidentManager incidentManager,
        IRepository<Entities.Orders.Order, Guid> orderRepository)
    {
        _settingProvider = settingProvider;
        _connectionStrings = connectionStrings;
        _sender = sender;
        _unitOfWorkManager = unitOfWorkManager;
        _incidentManager = incidentManager;
        _orderRepository = orderRepository;
    }

    public async Task<OutboxOptions> GetOptionsAsync()
    {
        var options = new OutboxOptions
        {
            MaxAttempts = await _settingProvider.GetAsync<int>(SahamSettings.Notifications.MaxAttempts),
            InitialRetryDelay = TimeSpan.FromSeconds(await _settingProvider.GetAsync<int>(SahamSettings.Notifications.RetryBaseSeconds)),
            MaximumRetryDelay = TimeSpan.FromSeconds(await _settingProvider.GetAsync<int>(SahamSettings.Notifications.MaxRetryDelaySeconds)),
            LeaseDuration = TimeSpan.FromSeconds(await _settingProvider.GetAsync<int>(SahamSettings.Notifications.LeaseSeconds)),
            SendTimeout = TimeSpan.FromSeconds(await _settingProvider.GetAsync<int>(SahamSettings.Notifications.SendTimeoutSeconds))
        };
        options.Validate(); // fails loudly on a bad configuration
        return options;
    }

    public async Task<NotificationOutboxStore> CreateStoreAsync(OutboxOptions options)
    {
        var connectionString = await _connectionStrings.ResolveAsync();
        return new NotificationOutboxStore(async ct =>
        {
            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(ct);
            return (DbConnection)connection;
        }, options, OnDeadAsync);
    }

    public async Task<OutboxRunResult> RunOnceAsync(int maximumMessages, CancellationToken cancellationToken)
    {
        var options = await GetOptionsAsync();
        var processor = new OutboxProcessor(await CreateStoreAsync(options), _sender, options);
        return await processor.RunOnceAsync(maximumMessages, cancellationToken);
    }

    private async Task OnDeadAsync(IReadOnlyList<DeadNotification> dead)
    {
        foreach (var n in dead)
        {
            if (n.Template != NotificationTemplates.DeliveryCode || !n.OrderId.HasValue)
            {
                continue;
            }

            // Ops read the code to the customer instead (REQ-NTF-02); OpenAsync deduplicates.
            using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
            var order = await _orderRepository.GetAsync(n.OrderId.Value, includeDetails: false);
            await _incidentManager.OpenAsync(IncidentType.DeliveryCodeSmsFailed, IncidentSeverity.High, order.Id,
                orderId: order.Id, restaurantId: order.RestaurantId);
            await uow.CompleteAsync();
        }
    }
}

/* Last worker cycle, for the health endpoint (per process; each backend instance reports its own). */
public class NotificationWorkerHealth : ISingletonDependency
{
    private readonly object _gate = new();

    public DateTime? LastRunAt { get; private set; }
    public DateTime? LastErrorAt { get; private set; }
    public string? LastErrorCode { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public long LostLeases { get; private set; }

    public void Succeeded(DateTime now, int lostLeases)
    {
        lock (_gate)
        {
            LastRunAt = now;
            ConsecutiveFailures = 0;
            LostLeases += lostLeases;
        }
    }

    public void Failed(DateTime now, Exception error)
    {
        lock (_gate)
        {
            LastRunAt = now;
            LastErrorAt = now;
            LastErrorCode = error.GetType().Name; // the type only: messages may hold phone numbers or codes
            ConsecutiveFailures++;
        }
    }
}
