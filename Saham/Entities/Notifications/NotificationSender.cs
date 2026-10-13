using System;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Saham.Entities.Orders;
using Saham.Infrastructure.Outbox;
using Saham.Integrations.Push;
using Saham.Integrations.Sms;
using Saham.Localization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Localization;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Sms;
using Volo.Abp.Uow;

namespace Saham.Entities.Notifications;

/* SRS NotificationService.processDue for ONE claimed message (Phase 6 IOutboxSender): render the text
 * in the recipient's language (DER-NTF-03) and hand it to the channel's real provider (ISmsSender,
 * IPushSender — FCM when configured). It changes no state: NotificationOutboxStore records the outcome
 * under the claim's lease, and the processor bounds the call with SendTimeout.
 * The notification id is the provider idempotency key (SMS property / push collapse key), so a resend
 * after "provider accepted, ack lost" can be dropped by the provider. Delivery is at-least-once.
 * The delivery code is read from the order at send time and never stored or logged. */
public class NotificationSender : DomainService, IOutboxSender
{
    /* No per-user language yet: Arabic, the default (D6), until a preferred language is stored. */
    private const string DefaultCulture = "ar";

    private readonly ISmsSender _smsSender;
    private readonly IPushSender _pushSender;
    private readonly IStringLocalizer<SahamResource> _localizer;
    private readonly IRepository<Notification, Guid> _notificationRepository;
    private readonly IRepository<Order, Guid> _orderRepository;
    private readonly IStringEncryptionService _encryption;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly NotificationOptions _options;

    public NotificationSender(
        ISmsSender smsSender,
        IPushSender pushSender,
        IStringLocalizer<SahamResource> localizer,
        IRepository<Notification, Guid> notificationRepository,
        IRepository<Order, Guid> orderRepository,
        IStringEncryptionService encryption,
        IUnitOfWorkManager unitOfWorkManager,
        IOptions<NotificationOptions> options)
    {
        _smsSender = smsSender;
        _pushSender = pushSender;
        _localizer = localizer;
        _notificationRepository = notificationRepository;
        _orderRepository = orderRepository;
        _encryption = encryption;
        _unitOfWorkManager = unitOfWorkManager;
        _options = options.Value;
    }

    public async Task<DeliveryResult> SendAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        // Its own non-transactional unit of work: reads and the adapters' writes (SMS log, dead push
        // tokens) commit on their own; no database transaction stays open during the provider call.
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var notification = await _notificationRepository.FindAsync(message.EventKey);
        if (notification == null)
        {
            return DeliveryResult.PermanentFailure("NOTIFICATION_NOT_FOUND");
        }

        if (notification.ExpiresAt.HasValue && notification.ExpiresAt.Value <= Clock.Now)
        {
            return DeliveryResult.PermanentFailure("EXPIRED"); // e.g. a 30 s delivery offer
        }

        if (_options.SimulateFailure.Contains(notification.Channel))
        {
            return new DeliveryResult(false, DeliveryDisposition.RetryableFailure, ErrorCode: "SIMULATED_FAILURE");
        }

        var text = await RenderAsync(notification);
        string? providerMessageId = null;
        try
        {
            if (notification.Channel == NotificationChannel.Sms)
            {
                var sms = new SmsMessage(notification.Phone!, text);
                sms.Properties[LoggingSmsSender.IdempotencyKeyProperty] = notification.Id.ToString("N");
                await _smsSender.SendAsync(sms).WaitAsync(cancellationToken);
            }
            else
            {
                using (CultureHelper.Use(DefaultCulture))
                {
                    providerMessageId = await _pushSender.SendAsync(new PushMessage(notification.UserId!.Value, _localizer["Notify:Title"],
                        text, notification.HighPriority, notification.OrderId, notification.Id.ToString("N"))).WaitAsync(cancellationToken);
                }
            }
        }
        catch (FirebaseMessagingException ex)
        {
            return Classify(ex);
        }

        await uow.CompleteAsync(cancellationToken);
        return DeliveryResult.Success(providerMessageId);
    }

    /* FCM's contract: quota / unavailable / internal are temporary (with its Retry-After as the minimum
     * wait); a malformed message or a bad sender setup will not get better by retrying. */
    private static DeliveryResult Classify(FirebaseMessagingException ex)
    {
        switch (ex.MessagingErrorCode)
        {
            case MessagingErrorCode.QuotaExceeded:
            case MessagingErrorCode.Unavailable:
            case MessagingErrorCode.Internal:
                var retryAfter = RetryAfterOf(ex.HttpResponse?.Headers.RetryAfter);
                return retryAfter.HasValue
                    ? DeliveryResult.Retry(retryAfter.Value) with { ErrorCode = "FCM_" + ex.MessagingErrorCode }
                    : new DeliveryResult(false, DeliveryDisposition.RetryableFailure, ErrorCode: "FCM_" + ex.MessagingErrorCode);
            case MessagingErrorCode.InvalidArgument:
            case MessagingErrorCode.SenderIdMismatch:
            case MessagingErrorCode.ThirdPartyAuthError:
                return DeliveryResult.PermanentFailure("FCM_" + ex.MessagingErrorCode);
            default:
                return new DeliveryResult(false, DeliveryDisposition.RetryableFailure, ErrorCode: "FCM_ERROR");
        }
    }

    /* A provider wait longer than a day is capped at a day (DeliveryResult's limit); never shortened. */
    public static TimeSpan? RetryAfterOf(RetryConditionHeaderValue? header, DateTimeOffset? now = null)
    {
        var wait = header?.Delta ?? (header?.Date is { } date ? date - (now ?? DateTimeOffset.UtcNow) : null);
        if (wait is not { } w || w <= TimeSpan.Zero)
        {
            return null;
        }

        return w > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : w;
    }

    private async Task<string> RenderAsync(Notification notification)
    {
        var args = notification.GetArgs().Cast<object>().ToList();
        if (notification.Template == NotificationTemplates.DeliveryCode)
        {
            var order = await _orderRepository.GetAsync(notification.OrderId!.Value, includeDetails: false);
            args.Add(_encryption.Decrypt(order.DeliveryOtpProtected!) ?? string.Empty);
        }

        using (CultureHelper.Use(DefaultCulture))
        {
            return _localizer[notification.Template, args.ToArray()];
        }
    }
}
