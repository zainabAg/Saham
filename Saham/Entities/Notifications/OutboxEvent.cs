using System;
using System.Collections.Generic;
using System.Text.Json;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities;

namespace Saham.Entities.Notifications;

/* SRS table outbox_events (I-6): a business event written in the same transaction as the change
 * that caused it, so it can never be lost or sent for a change that was rolled back.
 * NotificationDispatchWorker publishes it (turns it into notifications) and stamps PublishedAt. */
public class OutboxEvent : BasicAggregateRoot<long>, IHasCreationTime
{
    public const int MaxPublishAttempts = 5;

    public string EventType { get; private set; }

    /* The event's identity for deduplication, e.g. "ORDER_PICKED_UP:{order}". Notifications
     * carry it, and their unique key stops the same event being notified twice. */
    public string EventKey { get; private set; }

    public string AggregateType { get; private set; }

    public Guid AggregateId { get; private set; }

    /* jsonb: flat string values (ids, numbers as text). */
    public string Payload { get; private set; }

    public DateTime CreationTime { get; private set; }

    public DateTime? PublishedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    protected OutboxEvent()
    {
        EventType = null!;
        EventKey = null!;
        AggregateType = null!;
        Payload = null!;
    }

    internal OutboxEvent(string eventType, string eventKey, string aggregateType, Guid aggregateId,
        IReadOnlyDictionary<string, string?> payload, DateTime now)
    {
        EventType = eventType;
        EventKey = eventKey;
        AggregateType = aggregateType;
        AggregateId = aggregateId;
        Payload = JsonSerializer.Serialize(payload);
        CreationTime = now;
    }

    public IReadOnlyDictionary<string, string?> GetPayload() =>
        JsonSerializer.Deserialize<Dictionary<string, string?>>(Payload) ?? new Dictionary<string, string?>();

    public string? Get(string name) => GetPayload().GetValueOrDefault(name);

    public void MarkPublished(DateTime now)
    {
        PublishedAt = now;
        LastError = null;
    }

    /* A broken event is retried a few times, then set aside (published with its error) so it never
     * blocks the queue. */
    public void MarkFailed(string error, DateTime now)
    {
        Attempts++;
        LastError = error.Length > 1024 ? error[..1024] : error;
        if (Attempts >= MaxPublishAttempts)
        {
            PublishedAt = now;
        }
    }
}
