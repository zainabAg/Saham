using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Saham.Entities.Notifications;
using Volo.Abp.DependencyInjection;

namespace Saham.Integrations.Push;

/* The Firebase app, created once from the service account (FirebaseOptions). */
public class FirebaseMessagingProvider : ISingletonDependency
{
    private readonly Lazy<FirebaseMessaging> _messaging;

    public FirebaseMessagingProvider(IOptions<FirebaseOptions> options)
    {
        _messaging = new Lazy<FirebaseMessaging>(() =>
        {
            var o = options.Value;
            var json = string.IsNullOrWhiteSpace(o.CredentialJson) ? File.ReadAllText(o.CredentialPath!) : o.CredentialJson!;
            var credential = CredentialFactory.FromJson<ServiceAccountCredential>(json).ToGoogleCredential();
            var app = FirebaseApp.Create(new AppOptions { Credential = credential }, "saham");
            return FirebaseMessaging.GetMessaging(app);
        });
    }

    public FirebaseMessaging Messaging => _messaging.Value;
}

/* SRS PushProvider over Firebase Cloud Messaging: one message to every device of the user.
 * - high priority wakes a sleeping phone (new order on the tablet, a delivery offer);
 * - tokens FCM calls dead are removed, so an uninstalled app is not tried again;
 * - a user without any device counts as sent (nothing to retry); if every device failed for a
 *   temporary reason the exception lets NotificationSender retry later.
 * Registered instead of LoggingPushSender only when Firebase is configured (SahamModule). */
public class FcmPushSender : IPushSender
{
    public const string ProviderName = "fcm";
    public const string NoDevice = "fcm-no-device";

    private static readonly MessagingErrorCode[] DeadTokenErrors =
    {
        MessagingErrorCode.Unregistered,
        MessagingErrorCode.InvalidArgument,
        MessagingErrorCode.SenderIdMismatch
    };

    private readonly FirebaseMessagingProvider _firebase;
    private readonly UserDeviceManager _deviceManager;
    private readonly ILogger<FcmPushSender> _logger;

    public FcmPushSender(FirebaseMessagingProvider firebase, UserDeviceManager deviceManager, ILogger<FcmPushSender> logger)
    {
        _firebase = firebase;
        _deviceManager = deviceManager;
        _logger = logger;
    }

    public async Task<string?> SendAsync(PushMessage message)
    {
        var tokens = await _deviceManager.GetTokensAsync(message.UserId);
        if (tokens.Count == 0)
        {
            return NoDevice;
        }

        var data = new Dictionary<string, string> { ["priority"] = message.HighPriority ? "high" : "normal" };
        if (message.OrderId.HasValue)
        {
            data["orderId"] = message.OrderId.Value.ToString();
        }

        var response = await _firebase.Messaging.SendEachForMulticastAsync(new MulticastMessage
        {
            Tokens = tokens,
            Notification = new FirebaseAdmin.Messaging.Notification { Title = message.Title, Body = message.Body },
            Data = data,
            Android = new AndroidConfig { Priority = message.HighPriority ? Priority.High : Priority.Normal },
            Apns = new ApnsConfig
            {
                Headers = new Dictionary<string, string> { ["apns-priority"] = message.HighPriority ? "10" : "5" }
            }
        });

        var dead = new List<string>();
        var errors = new List<string>();
        for (var i = 0; i < response.Responses.Count; i++)
        {
            var result = response.Responses[i];
            if (result.IsSuccess)
            {
                continue;
            }

            if (result.Exception.MessagingErrorCode is { } code && DeadTokenErrors.Contains(code))
            {
                dead.Add(tokens[i]);
            }
            else
            {
                errors.Add(result.Exception.Message);
            }
        }

        if (dead.Count > 0)
        {
            _logger.LogInformation("Removing {Count} dead push token(s) of user {UserId}", dead.Count, message.UserId);
            await _deviceManager.RemoveTokensAsync(dead);
        }

        var sent = response.Responses.Where(r => r.IsSuccess).Select(r => r.MessageId).ToList();
        if (sent.Count > 0)
        {
            return Truncate(string.Join(",", sent));
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("FCM: " + errors.First());
        }

        return NoDevice; // every token was dead
    }

    private static string Truncate(string value) => value.Length > 128 ? value[..128] : value;
}
