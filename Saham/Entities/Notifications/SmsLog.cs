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

    /* Phase 6: the sender's idempotency key (the notification id). A resend after a crash between the
     * provider's acceptance and the queue's ack carries the same key and is dropped (unique index). */
    public string? IdempotencyKey { get; private set; }

    protected SmsLog()
    {
    }

    public SmsLog(Guid id, string phoneNumber, string text, string? provider, string? idempotencyKey = null)
        : base(id)
    {
        PhoneNumber = phoneNumber;
        Text = text;
        Provider = provider;
        Status = SmsLogStatus.Queued;
        IdempotencyKey = idempotencyKey;
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
