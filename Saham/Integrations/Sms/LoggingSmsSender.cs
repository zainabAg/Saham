using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Saham.Entities.Notifications;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Sms;

namespace Saham.Integrations.Sms;

/* SMS adapter used until the client picks a provider (EXT-02):
 * messages are written to the SmsLog table and read from the admin panel.
 * A real provider replaces this class and keeps writing the log.
 * Phase 6: honours SmsMessage.Properties["IdempotencyKey"] like a provider with idempotency would —
 * a second message with the same key is accepted without a second log row (no second "SMS"). */
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(ISmsSender))]
public class LoggingSmsSender : ISmsSender, ITransientDependency
{
    public const string ProviderName = "log";
    public const string IdempotencyKeyProperty = "IdempotencyKey";

    private readonly IRepository<SmsLog, Guid> _smsLogRepository;
    private readonly IGuidGenerator _guidGenerator;

    public LoggingSmsSender(IRepository<SmsLog, Guid> smsLogRepository, IGuidGenerator guidGenerator)
    {
        _smsLogRepository = smsLogRepository;
        _guidGenerator = guidGenerator;
    }

    public async Task SendAsync(SmsMessage smsMessage)
    {
        var key = smsMessage.Properties.TryGetValue(IdempotencyKeyProperty, out var value) ? value?.ToString() : null;
        if (key != null && await _smsLogRepository.AnyAsync(x => x.IdempotencyKey == key))
        {
            return; // already accepted once: the provider-side duplicate drop
        }

        var log = new SmsLog(_guidGenerator.Create(), smsMessage.PhoneNumber, smsMessage.Text, ProviderName, key);
        log.MarkSent(providerMessageId: null);
        try
        {
            await _smsLogRepository.InsertAsync(log, autoSave: true);
        }
        catch (DbUpdateException) when (key != null)
        {
            // Two attempts with the same key at once: the unique index kept one.
        }
    }
}
