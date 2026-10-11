using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Support;

public enum ContactChannel
{
    OpsPhone = 0,
    InAppVoice = 1
}

public enum ContactOutcome
{
    Answered = 0,
    NoAnswer = 1,
    CustomerOffline = 2,
    Failed = 3
}

/* A recorded attempt to reach the customer (SRS table contact_attempts). No phone number is
 * stored here or given to the driver (TD-03). "Not received" needs at least one unanswered
 * attempt as evidence (BR-063). */
public class ContactAttempt : CreationAuditedAggregateRoot<Guid>
{
    public Guid OrderId { get; private set; }

    public Guid? AssignmentId { get; private set; }

    public Guid InitiatedBy { get; private set; }

    public ContactChannel Channel { get; private set; }

    public ContactOutcome Outcome { get; private set; }

    public DateTime StartedAt { get; private set; }

    public string? Note { get; private set; }

    protected ContactAttempt()
    {
    }

    public ContactAttempt(Guid id, Guid orderId, Guid? assignmentId, Guid initiatedBy, ContactChannel channel,
        ContactOutcome outcome, DateTime startedAt, string? note)
        : base(id)
    {
        OrderId = orderId;
        AssignmentId = assignmentId;
        InitiatedBy = initiatedBy;
        Channel = channel;
        Outcome = outcome;
        StartedAt = startedAt;
        Note = note;
    }

    public bool IsUnanswered => Outcome is ContactOutcome.NoAnswer or ContactOutcome.CustomerOffline;
}
