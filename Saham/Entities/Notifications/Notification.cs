using System;
using System.Collections.Generic;
using System.Text.Json;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Notifications;

/* SRS table notifications (I-6): one message to one recipient on one channel.
 * Unique (EventKey, Channel, UserId): the same event never reaches the same person twice
 * on the same channel, however often it is published.
 * The text is not stored: Template + Args are rendered in the recipient's language when sent
 * (DER-NTF-03), so a delivery code never sits in this table. */
public class Notification : CreationAuditedAggregateRoot<Guid>
{
    public const int ErrorMaxLength = 1024;

    public string EventKey { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public Guid? UserId { get; private set; }

    /* SMS only: the number at the time of the event (e.g. the recipient named on the order). */
    public string? Phone { get; private set; }

    /* Localization key of the text, e.g. "Notify:OrderAccepted". */
    public string Template { get; private set; }

    /* jsonb: { "args": [...] } — the template arguments. */
    public string Payload { get; private set; }

    public Guid? OrderId { get; private set; }

    public bool HighPriority { get; private set; }

    /* A message that means nothing after a moment (a 30 s delivery offer) is dropped, not retried. */
    public DateTime? ExpiresAt { get; private set; }

    public NotificationStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTime NextAttemptAt { get; private set; }

    public DateTime? SentAt { get; private set; }

    public string? ProviderMessageId { get; private set; }

    /* Safe error code only (e.g. PROVIDER_TIMEOUT_OUTCOME_UNKNOWN), never provider text or a phone number. */
    public string? LastError { get; private set; }

    /* Phase 6: the worker that claimed the message owns it until LeaseUntil (database time, UTC).
     * State changes are conditional UPDATEs in NotificationOutboxStore, so an old worker cannot
     * overwrite a newer claim. */
    public Guid? LeaseToken { get; private set; }

    public DateTime? LeaseUntil { get; private set; }

    protected Notification()
    {
        EventKey = null!;
        Template = null!;
        Payload = null!;
    }

    internal Notification(Guid id, string eventKey, NotificationChannel channel, Guid? userId, string? phone,
        string template, IReadOnlyList<string> args, Guid? orderId, bool highPriority, DateTime? expiresAt, DateTime now)
        : base(id)
    {
        if (channel == NotificationChannel.Sms && phone.IsNullOrWhiteSpace())
        {
            throw new ArgumentException("An SMS needs a phone number.", nameof(phone));
        }

        EventKey = eventKey;
        Channel = channel;
        UserId = userId;
        Phone = phone;
        Template = template;
        Payload = JsonSerializer.Serialize(new NotificationPayload(args));
        OrderId = orderId;
        HighPriority = highPriority;
        ExpiresAt = expiresAt;
        Status = NotificationStatus.Queued;
        NextAttemptAt = now;
    }

    public IReadOnlyList<string> GetArgs() =>
        JsonSerializer.Deserialize<NotificationPayload>(Payload)?.Args ?? (IReadOnlyList<string>)Array.Empty<string>();

    /* Ops may give a dead message another round (e.g. after the provider came back). The row, its
     * EventKey and its id (the provider idempotency key) stay the same; a Sent message is never resent.
     * Sending, retries and giving up are NotificationOutboxStore's (claim / lease / conditional ack). */
    public void Requeue(DateTime now)
    {
        if (Status != NotificationStatus.Dead)
        {
            throw new BusinessException(SahamErrorCodes.NotificationNotDead);
        }

        Status = NotificationStatus.Queued;
        Attempts = 0;
        NextAttemptAt = now;
        ExpiresAt = null;
        LeaseToken = null;
        LeaseUntil = null;
    }
}

public record NotificationPayload(IReadOnlyList<string> Args);
