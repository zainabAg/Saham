using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Notifications;

/* Every SMS the system sends. Until a real SMS provider is chosen (EXT-02),
 * this table is the delivery channel and is shown in the admin panel. */
public class SmsLog : CreationAuditedAggregateRoot<Guid>
{
    public string PhoneNumber { get; private set; }

    public string Text { get; private set; }

    public SmsLogStatus Status { get; private set; }

    public string? Provider { get; private set; }

    public string? ProviderMessageId { get; private set; }

    public string? LastError { get; private set; }

    protected SmsLog()
    {
    }

    public SmsLog(Guid id, string phoneNumber, string text, string? provider)
        : base(id)
    {
        PhoneNumber = phoneNumber;
        Text = text;
        Provider = provider;
        Status = SmsLogStatus.Queued;
    }

    public void MarkSent(string? providerMessageId)
    {
        Status = SmsLogStatus.Sent;
        ProviderMessageId = providerMessageId;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        Status = SmsLogStatus.Failed;
        LastError = error;
    }
}
