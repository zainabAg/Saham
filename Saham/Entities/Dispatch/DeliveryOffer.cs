using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace Saham.Entities.Dispatch;

/* One offer of one order to one driver, valid for 30 seconds of server time (BR-028).
 * The full list is kept: "who was offered, when, and what happened" is the evidence in
 * disputes and the source of the "who declined" report (SRS 13.5).
 * Partial unique indexes: one pending offer per order and per driver; never the same
 * order to the same driver twice. */
public class DeliveryOffer : AggregateRoot<Guid>
{
    public Guid OrderId { get; private set; }

    public Guid DriverId { get; private set; }

    public int RoundNo { get; private set; }

    public int DistanceToRestaurantM { get; private set; }

    public bool IsBatch { get; private set; }

    public OfferStatus Status { get; private set; }

    public DateTime OfferedAt { get; private set; }

    public DateTime ExpiresAt { get; private set; }

    public DateTime? RespondedAt { get; private set; }

    protected DeliveryOffer()
    {
    }

    internal DeliveryOffer(Guid id, Guid orderId, Guid driverId, int roundNo, int distanceToRestaurantM, DateTime now, DateTime expiresAt)
        : base(id)
    {
        OrderId = orderId;
        DriverId = driverId;
        RoundNo = roundNo;
        DistanceToRestaurantM = distanceToRestaurantM;
        Status = OfferStatus.Pending;
        OfferedAt = now;
        ExpiresAt = expiresAt;
        AddLocalEvent(new DriverNotificationEto(driverId, DriverNotificationKind.OfferCreated, orderId, id, expiresAt));
    }

    public bool IsExpired(DateTime now) => now > ExpiresAt;

    /* BR-029: only a pending, unexpired offer can be accepted (EX-05: late accept → 409). */
    internal void Accept(DateTime now)
    {
        EnsurePending();
        if (IsExpired(now))
        {
            throw new BusinessException(SahamErrorCodes.OfferExpired);
        }

        Status = OfferStatus.Accepted;
        RespondedAt = now;
    }

    internal void Decline(DateTime now)
    {
        EnsurePending();
        Status = OfferStatus.Declined;
        RespondedAt = now;
    }

    internal void Expire(DateTime now)
    {
        if (Status == OfferStatus.Pending)
        {
            Status = OfferStatus.Expired;
            RespondedAt = now;
        }
    }

    /* The order was cancelled, put on hold or assigned by hand. */
    internal void Cancel(DateTime now)
    {
        if (Status == OfferStatus.Pending)
        {
            Status = OfferStatus.Cancelled;
            RespondedAt = now;
            AddLocalEvent(new DriverNotificationEto(DriverId, DriverNotificationKind.OfferCancelled, OrderId, Id, null));
        }
    }

    private void EnsurePending()
    {
        if (Status != OfferStatus.Pending)
        {
            throw new BusinessException(SahamErrorCodes.OfferNotPending).WithData("status", Status.ToString());
        }
    }
}
