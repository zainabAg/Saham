using System;
using System.Linq;
using System.Threading.Tasks;
using Saham.Entities.Pricing;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Saham.Entities.Coupons;

/* Coupons from creation to use (BR-010..BR-012, BR-072):
 *   - the owner creates active coupons; a restaurant manager requests one for their restaurant,
 *     funded by the restaurant, which stays PENDING_APPROVAL until the owner decides;
 *   - quotes check a code without reserving it; the order reserves it through
 *     CouponReservationCoordinator (coupon row locked, total and per-customer limits counted after
 *     the lock), so two orders can never both take the last use;
 *   - the reservation is consumed on delivery / "not received" / a late customer cancellation,
 *     and released (the use given back) on any other cancellation or a failed payment. */
public class CouponManager : DomainService
{
    private readonly IRepository<Coupon, Guid> _couponRepository;
    private readonly IRepository<CouponRedemption, Guid> _redemptionRepository;
    private readonly CouponReservationCoordinator _reservations;

    public CouponManager(
        IRepository<Coupon, Guid> couponRepository,
        IRepository<CouponRedemption, Guid> redemptionRepository,
        CouponReservationCoordinator reservations)
    {
        _couponRepository = couponRepository;
        _redemptionRepository = redemptionRepository;
        _reservations = reservations;
    }

    public async Task<Coupon> CreateByOwnerAsync(Guid ownerId, string code, CouponDiscountType type, long value, long? maxDiscount,
        long minSubtotal, DateTime startsAt, DateTime endsAt, int? limitTotal, int? limitPerCustomer,
        CouponFundedBy fundedBy, bool? stackable, Guid? restaurantId)
    {
        await EnsureCodeFreeAsync(code);
        var coupon = new Coupon(GuidGenerator.Create(), code, type, value, maxDiscount, minSubtotal, startsAt, endsAt,
            limitTotal, limitPerCustomer, fundedBy, stackable, restaurantId);
        coupon.ActivateByOwner(ownerId);
        return await _couponRepository.InsertAsync(coupon, autoSave: true);
    }

    /* BR-072: a restaurant's own coupon is always funded by that restaurant. */
    public async Task<Coupon> RequestByRestaurantAsync(Guid managerId, Guid restaurantId, string code, CouponDiscountType type,
        long value, long? maxDiscount, long minSubtotal, DateTime startsAt, DateTime endsAt, int? limitTotal, int? limitPerCustomer)
    {
        await EnsureCodeFreeAsync(code);
        var coupon = new Coupon(GuidGenerator.Create(), code, type, value, maxDiscount, minSubtotal, startsAt, endsAt,
            limitTotal, limitPerCustomer, CouponFundedBy.Restaurant, stackableWithFreeDelivery: null, restaurantId);
        coupon.MarkRequested(managerId);
        return await _couponRepository.InsertAsync(coupon, autoSave: true);
    }

    /* BR-010 without the reservation: the quote shows the discount, the order takes the use. */
    public async Task<Coupon> FindValidAsync(string code, Guid customerId, Guid restaurantId, long itemsSubtotal, DateTime now)
    {
        var normalized = Coupon.NormalizeCode(code);
        var coupon = await _couponRepository.FirstOrDefaultAsync(c => c.Code == normalized)
                     ?? throw new BusinessException(SahamErrorCodes.CouponNotFound);

        if (!coupon.IsUsableAt(now))
        {
            throw new BusinessException(SahamErrorCodes.CouponNotValidNow);
        }

        if (coupon.IsUsedUp)
        {
            throw new BusinessException(SahamErrorCodes.CouponUsageLimitReached);
        }

        if (coupon.RestaurantId.HasValue && coupon.RestaurantId != restaurantId)
        {
            throw new BusinessException(SahamErrorCodes.CouponNotForRestaurant);
        }

        if (itemsSubtotal < coupon.MinSubtotal)
        {
            throw new BusinessException(SahamErrorCodes.CouponBelowMinSubtotal).WithData("minSubtotal", coupon.MinSubtotal);
        }

        if (coupon.UsageLimitPerCustomer.HasValue &&
            await CustomerUsesAsync(coupon.Id, customerId) >= coupon.UsageLimitPerCustomer.Value)
        {
            throw new BusinessException(SahamErrorCodes.CouponUsageLimitReached);
        }

        return coupon;
    }

    /* The lifecycle of a use lives in CouponReservationCoordinator only, inside the caller's transaction. */
    public Task LockForOrderAsync(Guid couponId) => _reservations.LockAsync(couponId);

    public Task ReserveAsync(QuoteCouponSnapshot snapshot, Guid orderId, Guid customerId, Guid restaurantId, long itemsSubtotal) =>
        _reservations.ReserveAsync(snapshot, orderId, customerId, restaurantId, itemsSubtotal);

    public Task ConsumeAsync(Guid orderId) => _reservations.ConsumeAsync(orderId);

    /* The use goes back to the coupon (cancellation not caused by the customer, payment failed). */
    public Task ReleaseAsync(Guid orderId) => _reservations.ReleaseAsync(orderId);

    /* BR-048: the discount after an item was removed (never more than before). */
    public Task UpdateAmountAsync(Guid orderId, long amount) => _reservations.UpdateAmountAsync(orderId, amount);

    private Task<int> CustomerUsesAsync(Guid couponId, Guid customerId) =>
        _redemptionRepository.CountAsync(r => r.CouponId == couponId && r.CustomerId == customerId &&
                                              r.Status != CouponRedemptionStatus.Released);

    private async Task EnsureCodeFreeAsync(string code)
    {
        var normalized = Coupon.NormalizeCode(code);
        if (await _couponRepository.AnyAsync(c => c.Code == normalized))
        {
            throw new BusinessException(SahamErrorCodes.CouponCodeTaken);
        }
    }
}
