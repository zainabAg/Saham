using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace Saham.Integrations.Push;

/* Push adapter used until Firebase keys are available (same idea as LoggingSmsSender):
 * the message is written to the application log; the Notifications table keeps the record. */
[ExposeServices(typeof(IPushSender))]
public class LoggingPushSender : IPushSender, ITransientDependency
{
    public const string ProviderName = "log";

    private readonly ILogger<LoggingPushSender> _logger;

    public LoggingPushSender(ILogger<LoggingPushSender> logger)
    {
        _logger = logger;
    }

    public Task<string?> SendAsync(PushMessage message)
    {
        _logger.LogInformation("PUSH to {UserId} (high priority: {High}, order: {OrderId}): {Title} - {Body}",
            message.UserId, message.HighPriority, message.OrderId, message.Title, message.Body);
        return Task.FromResult<string?>($"{ProviderName}-{Guid.NewGuid():N}");
    }
}
