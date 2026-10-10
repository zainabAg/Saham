using System;
using Volo.Abp.Domain.Entities;

namespace Saham.Entities.Orders;

/* A documented change to a placed order — the only way prices change after ordering
 * (BR-014, BR-048, BR-060). Before/after are JSON snapshots of the affected values. */
public class OrderModification : Entity<Guid>
{
    public Guid OrderId { get; private set; }

    public OrderModificationType ModificationType { get; private set; }

    public string Before { get; private set; }

    public string After { get; private set; }

    /* Change of the amount due (negative = the customer pays less). */
    public long AmountDelta { get; private set; }

    public ActorType RequestedByType { get; private set; }

    public Guid? RequestedBy { get; private set; }

    public DateTime CreationTime { get; private set; }

    protected OrderModification()
    {
        Before = null!;
        After = null!;
    }

    internal OrderModification(
        Guid id,
        Guid orderId,
        OrderModificationType modificationType,
        string before,
        string after,
        long amountDelta,
        ActorType requestedByType,
        Guid? requestedBy,
        DateTime creationTime)
        : base(id)
    {
        OrderId = orderId;
        ModificationType = modificationType;
        Before = before;
        After = after;
        AmountDelta = amountDelta;
        RequestedByType = requestedByType;
        RequestedBy = requestedBy;
        CreationTime = creationTime;
    }
}
