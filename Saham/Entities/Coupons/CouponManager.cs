using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Saham.Entities.Coupons;

/* Coupons from creation to use (BR-010..BR-012, BR-072):
 *   - the owner creates active coupons; a restaurant manager requests one for their restaurant,
 *     funded by the restaurant, which stays PENDING_APPROVAL until the owner decides;
 *   - quotes check a code without reserving it; the order reserves it atomically
 *     (UPDATE … SET used_count = used_count + 1 WHERE used_count < limit), so two orders can never
 *     both take the last use;
 *   - the reservation is consumed on delivery / "not received" / a late customer cancellation,
 *     and released (the use given back) on any other cancellation or a failed payment. */
public class CouponManager : DomainService
{
    private readonly IRepository<Coupon, Guid> _couponRepository;
    private readonly IRepository<CouponRedemption, Guid> _redemptionRepository;

    public CouponManager(IRepository<Coupon, Guid> couponRepository, IRepository<CouponRedemption, Guid> redemptionRepository)
    {
        _couponRepository = couponRepository;
        _redemptionRepository = redemptionRepository;
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

    /* Inside the order transaction. The conditional UPDATE is the lock (BR-010 race on the last use). */
    public async Task ReserveAsync(Guid couponId, Guid orderId, Guid customerId, long amount)
    {
        var taken = await (await _couponRepository.GetDbSetAsync())
            .Where(c => c.Id == couponId && c.Status == CouponStatus.Active &&
                        (c.UsageLimitTotal == null || c.UsedCount < c.UsageLimitTotal))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedCount, c => c.UsedCount + 1));
        if (taken == 0)
        {
            throw new BusinessException(SahamErrorCodes.CouponUsageLimitReached);
        }

        await _redemptionRepository.InsertAsync(new CouponRedemption(GuidGenerator.Create(), couponId, orderId, customerId, amount));
    }

    public async Task ConsumeAsync(Guid orderId)
    {
        var redemption = await _redemptionRepository.FindAsync(r => r.OrderId == orderId);
        if (redemption != null && redemption.Consume())
        {
            await _redemptionRepository.UpdateAsync(redemption);
        }
    }

    /* The use goes back to the coupon (cancellation not caused by the customer, payment failed). */
    public async Task ReleaseAsync(Guid orderId)
    {
        var redemption = await _redemptionRepository.FindAsync(r => r.OrderId == orderId);
        if (redemption == null || !redemption.Release())
        {
            return;
        }

        await _redemptionRepository.UpdateAsync(redemption);
        await (await _couponRepository.GetDbSetAsync())
            .Where(c => c.Id == redemption.CouponId && c.UsedCount > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedCount, c => c.UsedCount - 1));
    }

    /* BR-048: the discount after an item was removed (never more than before). */
    public async Task UpdateAmountAsync(Guid orderId, long amount)
    {
        var redemption = await _redemptionRepository.FindAsync(r => r.OrderId == orderId);
        if (redemption != null && redemption.Amount != amount)
        {
            redemption.UpdateAmount(amount);
            await _redemptionRepository.UpdateAsync(redemption);
        }
    }

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
