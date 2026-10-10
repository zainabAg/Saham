using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Pricing;

/* A price quote shown before the customer confirms (SRS table quotes, BR-001).
 * Valid for a few minutes and usable for one order only: "what the customer saw
 * is what is stored" (SRS 13.5, orders 1—1 quotes). */
public class Quote : CreationAuditedAggregateRoot<Guid>
{
    public Guid CustomerId { get; private set; }

    public Guid RestaurantId { get; private set; }

    public Guid AddressId { get; private set; }

    public Guid PricingPlanId { get; private set; }

    public OrderType OrderType { get; private set; }

    public DateTime? ScheduledFor { get; private set; }

    public long AmountDue { get; private set; }

    public QuoteSnapshot Snapshot { get; private set; }

    public string PayloadHash { get; private set; }

    public DateTime ExpiresAt { get; private set; }

    public Guid? ConsumedOrderId { get; private set; }

    protected Quote()
    {
        Snapshot = null!;
        PayloadHash = null!;
    }

    internal Quote(
        Guid id,
        Guid customerId,
        OrderType orderType,
        DateTime? scheduledFor,
        QuoteSnapshot snapshot,
        DateTime expiresAt)
        : base(id)
    {
        CustomerId = customerId;
        RestaurantId = snapshot.RestaurantId;
        AddressId = snapshot.Address.AddressId;
        PricingPlanId = snapshot.PricingPlanId;
        OrderType = orderType;
        ScheduledFor = scheduledFor;
        AmountDue = snapshot.AmountDue;
        Snapshot = snapshot;
        PayloadHash = snapshot.ComputeHash();
        ExpiresAt = expiresAt;
    }

    public bool IsExpired(DateTime now) => now >= ExpiresAt;

    public bool IsConsumed => ConsumedOrderId.HasValue;

    /* One quote, one order (orders.quote_id is unique). */
    public void MarkConsumed(Guid orderId)
    {
        if (IsConsumed)
        {
            throw new BusinessException(SahamErrorCodes.QuoteAlreadyUsed);
        }

        ConsumedOrderId = orderId;
    }
}
