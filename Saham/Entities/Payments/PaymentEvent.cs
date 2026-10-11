using System;
using Volo.Abp.Domain.Entities;

namespace Saham.Entities.Payments;

/* Every webhook the provider sends (SRS table payment_events), kept as received.
 * UK (provider, provider_event_id): a repeated notification is recognised and ignored (UC-05 E3). */
public class PaymentEvent : BasicAggregateRoot<Guid>
{
    public string Provider { get; private set; }

    public string ProviderEventId { get; private set; }

    public Guid? PaymentId { get; private set; }

    public string EventType { get; private set; }

    /* jsonb: the raw body. */
    public string Payload { get; private set; }

    public bool SignatureValid { get; private set; }

    public DateTime ReceivedAt { get; private set; }

    public DateTime? ProcessedAt { get; private set; }

    public string? ProcessingError { get; private set; }

    protected PaymentEvent()
    {
        Provider = null!;
        ProviderEventId = null!;
        EventType = null!;
        Payload = null!;
    }

    internal PaymentEvent(Guid id, string provider, string providerEventId, string eventType, string payload,
        bool signatureValid, DateTime receivedAt)
        : base(id)
    {
        Provider = provider;
        ProviderEventId = providerEventId;
        EventType = eventType;
        Payload = payload;
        SignatureValid = signatureValid;
        ReceivedAt = receivedAt;
    }

    internal void MarkProcessed(Guid? paymentId, DateTime now, string? error = null)
    {
        PaymentId = paymentId;
        ProcessedAt = now;
        ProcessingError = error;
    }
}
