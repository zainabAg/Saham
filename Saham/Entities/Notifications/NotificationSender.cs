using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Saham.Entities.Orders;
using Saham.Entities.Support;
using Saham.Integrations.Push;
using Saham.Localization;
using Saham.Settings;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Localization;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Settings;
using Volo.Abp.Sms;

namespace Saham.Entities.Notifications;

/* SRS NotificationService.processDue for one message: render the text in the recipient's
 * language (DER-NTF-03), send it through the channel's provider, and record the outcome.
 * A failure is retried with growing gaps; the final failure of the delivery-code SMS opens an
 * incident for ops, who then read the code to the customer (REQ-NTF-02). */
public class NotificationSender : DomainService
{
    /* No per-user language yet: Arabic, the default (D6), until a preferred language is stored. */
    private const string DefaultCulture = "ar";

    private readonly ISmsSender _smsSender;
    private readonly IPushSender _pushSender;
    private readonly IStringLocalizer<SahamResource> _localizer;
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IStringEncryptionService _encryption;
    private readonly ISettingProvider _settingProvider;
    private readonly IncidentManager _incidentManager;
    private readonly NotificationOptions _options;

    public NotificationSender(
        ISmsSender smsSender,
        IPushSender pushSender,
        IStringLocalizer<SahamResource> localizer,
        IRepository<Order, Guid> orderRepository,
        IStringEncryptionService encryption,
        ISettingProvider settingProvider,
        IncidentManager incidentManager,
        IOptions<NotificationOptions> options)
    {
        _smsSender = smsSender;
        _pushSender = pushSender;
        _localizer = localizer;
        _orderRepository = orderRepository;
        _encryption = encryption;
        _settingProvider = settingProvider;
        _incidentManager = incidentManager;
        _options = options.Value;
    }

    public async Task SendAsync(Notification notification)
    {
        var now = Clock.Now;
        if (!notification.IsDue(now))
        {
            return;
        }

        if (notification.ExpiresAt.HasValue && notification.ExpiresAt.Value <= now)
        {
            notification.MarkExpired();
            return;
        }

        try
        {
            if (_options.SimulateFailure.Contains(notification.Channel))
            {
                throw new InvalidOperationException($"Simulated {notification.Channel} provider failure.");
            }

            var text = await RenderAsync(notification);
            string? providerMessageId = null;
            if (notification.Channel == NotificationChannel.Sms)
            {
                await _smsSender.SendAsync(new SmsMessage(notification.Phone!, text));
            }
            else
            {
                using (CultureHelper.Use(DefaultCulture))
                {
                    providerMessageId = await _pushSender.SendAsync(new PushMessage(notification.UserId!.Value, _localizer["Notify:Title"],
                        text, notification.HighPriority, notification.OrderId));
                }
            }

            notification.MarkSent(providerMessageId, now);
        }
        catch (Exception ex)
        {
            notification.MarkFailed(ex.Message,
                await _settingProvider.GetAsync<int>(SahamSettings.Notifications.MaxAttempts),
                await _settingProvider.GetAsync<int>(SahamSettings.Notifications.RetryBaseSeconds),
                now);

            if (notification.Status == NotificationStatus.Dead &&
                notification.Template == NotificationTemplates.DeliveryCode &&
                notification.OrderId.HasValue)
            {
                var order = await _orderRepository.GetAsync(notification.OrderId.Value, includeDetails: false);
                await _incidentManager.OpenAsync(IncidentType.DeliveryCodeSmsFailed, IncidentSeverity.High, order.Id,
                    orderId: order.Id, restaurantId: order.RestaurantId);
            }
        }
    }

    private async Task<string> RenderAsync(Notification notification)
    {
        var args = notification.GetArgs().Cast<object>().ToList();
        if (notification.Template == NotificationTemplates.DeliveryCode)
        {
            // The code is read from the order at send time and never stored in the notification.
            var order = await _orderRepository.GetAsync(notification.OrderId!.Value, includeDetails: false);
            args.Add(_encryption.Decrypt(order.DeliveryOtpProtected!) ?? string.Empty);
        }

        using (CultureHelper.Use(DefaultCulture))
        {
            return _localizer[notification.Template, args.ToArray()];
        }
    }
}
