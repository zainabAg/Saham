using System;
using System.Threading.Tasks;
using Saham.Entities.Notifications;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Sms;

namespace Saham.Integrations.Sms;

/* SMS adapter used until the client picks a provider (EXT-02):
 * messages are written to the SmsLog table and read from the admin panel.
 * A real provider replaces this class and keeps writing the log. */
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(ISmsSender))]
public class LoggingSmsSender : ISmsSender, ITransientDependency
{
    public const string ProviderName = "log";

    private readonly IRepository<SmsLog, Guid> _smsLogRepository;
    private readonly IGuidGenerator _guidGenerator;

    public LoggingSmsSender(IRepository<SmsLog, Guid> smsLogRepository, IGuidGenerator guidGenerator)
    {
        _smsLogRepository = smsLogRepository;
        _guidGenerator = guidGenerator;
    }

    public async Task SendAsync(SmsMessage smsMessage)
    {
        var log = new SmsLog(_guidGenerator.Create(), smsMessage.PhoneNumber, smsMessage.Text, ProviderName);
        log.MarkSent(providerMessageId: null);
        await _smsLogRepository.InsertAsync(log);
    }
}
