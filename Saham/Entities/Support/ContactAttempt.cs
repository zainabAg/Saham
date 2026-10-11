using System;
using Volo.Abp;
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
    Failed = 3,
    Ringing = 4   // an in-app call still waiting for the customer's answer
}

/* A recorded attempt to reach the customer (SRS table contact_attempts). No phone number is
 * stored here or given to the driver (TD-03). "Not received" needs at least one unanswered
 * attempt as evidence (BR-063).
 * An in-app call (VoiceCallService) is one attempt: Ringing → Answered → ended with its duration,
 * or Ringing → NoAnswer (declined / no answer in time) / Failed. */
public class ContactAttempt : CreationAuditedAggregateRoot<Guid>
{
    public Guid OrderId { get; private set; }

    public Guid? AssignmentId { get; private set; }

    public Guid InitiatedBy { get; private set; }

    public ContactChannel Channel { get; private set; }

    public ContactOutcome Outcome { get; private set; }

    public DateTime StartedAt { get; private set; }

    public DateTime? AnsweredAt { get; private set; }

    public DateTime? EndedAt { get; private set; }

    public int? DurationSeconds { get; private set; }

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
        if (outcome != ContactOutcome.Ringing)
        {
            EndedAt = startedAt;
        }
    }

    public bool IsUnanswered => Outcome is ContactOutcome.NoAnswer or ContactOutcome.CustomerOffline;

    /* Ringing or talking: signalling between the two apps is allowed. */
    public bool IsLive => Channel == ContactChannel.InAppVoice && EndedAt == null;

    public void Answer(DateTime now)
    {
        EnsureRinging();
        Outcome = ContactOutcome.Answered;
        AnsweredAt = now;
    }

    /* The customer refused, or nobody picked up in time. */
    public void Miss(DateTime now)
    {
        EnsureRinging();
        Outcome = ContactOutcome.NoAnswer;
        EndedAt = now;
    }

    /* Either side hung up. A call that was never answered counts as "no answer"; a technical
     * failure (no audio path) is recorded as such. */
    public void End(DateTime now, bool failed)
    {
        if (!IsLive)
        {
            throw new BusinessException(SahamErrorCodes.CallNotActive);
        }

        Outcome = failed ? ContactOutcome.Failed : AnsweredAt.HasValue ? ContactOutcome.Answered : ContactOutcome.NoAnswer;
        EndedAt = now;
        DurationSeconds = AnsweredAt.HasValue ? (int)Math.Round((now - AnsweredAt.Value).TotalSeconds) : 0;
    }

    private void EnsureRinging()
    {
        if (!IsLive || Outcome != ContactOutcome.Ringing)
        {
            throw new BusinessException(SahamErrorCodes.CallNotActive);
        }
    }
}
