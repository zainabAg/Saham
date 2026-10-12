using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Saham.Data;
using Saham.Entities.Customers;
using Saham.Entities.Pricing;
using Saham.Settings;
using Volo.Abp;
using Volo.Abp.Domain.Services;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Settings;

namespace Saham.Entities.Coupons;

/* Phase 3 (F05) — the only writer of coupon uses (BR-010, BR-011):
 *   - the coupon row is locked (SELECT … FOR UPDATE) until the caller's transaction ends, then the
 *     coupon, its terms, the time and this customer's uses are read again, so neither the total limit
 *     nor the per-customer limit can be passed by two orders at once;
 *   - Reserved and Consumed uses count, Released ones do not;
 *   - an exact replay of a reservation takes nothing more; release gives the use back once; a consumed
 *     use never comes back.
 * Joins the caller's ABP unit of work (never requiresNew): an order that fails rolls back its use.
 * Lock order across the project: customer balance lock → coupon row → redemption. */
public class CouponReservationCoordinator : DomainService
{
    private readonly IDbContextProvider<SahamDbContext> _contextProvider;
    private readonly ISettingProvider _settingProvider;

    public CouponReservationCoordinator(IDbContextProvider<SahamDbContext> contextProvider, ISettingProvider settingProvider)
    {
        _contextProvider = contextProvider;
        _settingProvider = settingProvider;
    }

    /* Taken by OrderManager BEFORE it re-prices, so the fresh quote is computed from coupon terms that
     * cannot change until commit, and a change surfaces as the normal PRICE_CHANGED with a real new quote. */
    public async Task LockAsync(Guid couponId)
    {
        RequireId(couponId, nameof(couponId));
        await LockCouponAsync(await GetTransactionalContextAsync(), couponId);
    }

    /* At order confirmation, with server-owned values from the quote. */
    public async Task ReserveAsync(QuoteCouponSnapshot expected, Guid orderId, Guid customerId, Guid restaurantId, long itemsSubtotal)
    {
        ArgumentNullException.ThrowIfNull(expected);
        RequireId(expected.CouponId, nameof(expected.CouponId));
        RequireId(orderId, nameof(orderId));
        RequireId(customerId, nameof(customerId));
        RequireId(restaurantId, nameof(restaurantId));
        if (itemsSubtotal < 0 || expected.Amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemsSubtotal));
        }

        var db = await GetTransactionalContextAsync();
        var coupon = await LockCouponAsync(db, expected.CouponId); // re-entrant if LockAsync ran first

        var existing = await db.CouponRedemptions.AsNoTracking().SingleOrDefaultAsync(r => r.OrderId == orderId);
        if (existing != null)
        {
            if (existing.CouponId == expected.CouponId && existing.CustomerId == customerId &&
                existing.Amount == expected.Amount && existing.Status != CouponRedemptionStatus.Released)
            {
                return; // the same reservation again: no second use
            }

            throw new BusinessException(SahamErrorCodes.CouponReservationConflict);
        }

        // The time after the lock: the coupon may have expired while this request waited for it.
        var now = Clock.Now;
        if (coupon.IsDeleted || coupon.ApprovedBy == null || !coupon.IsUsableAt(now))
        {
            throw new BusinessException(SahamErrorCodes.CouponNotValidNow);
        }

        if (coupon.RestaurantId.HasValue && coupon.RestaurantId != restaurantId)
        {
            throw new BusinessException(SahamErrorCodes.CouponNotForRestaurant);
        }

        if (itemsSubtotal < coupon.MinSubtotal)
        {
            throw new BusinessException(SahamErrorCodes.CouponBelowMinSubtotal).WithData("minSubtotal", coupon.MinSubtotal);
        }

        // All frozen terms, not only K: free-delivery stacking changes the total even with the same K.
        // OrderManager re-prices under this lock, so this is a guard; it has no new quote to offer.
        if (coupon.Code != expected.Code || coupon.DiscountType != expected.DiscountType ||
            coupon.Value != expected.Value || coupon.MaxDiscount != expected.MaxDiscount ||
            coupon.FundedBy != expected.FundedBy ||
            coupon.StackableWithFreeDelivery != expected.StackableWithFreeDelivery ||
            coupon.DiscountFor(itemsSubtotal) != expected.Amount)
        {
            throw new BusinessException(SahamErrorCodes.PriceChanged);
        }

        // Counted after the lock: the check that was missing (F05).
        var customerUses = await db.CouponRedemptions.AsNoTracking().CountAsync(r =>
            r.CouponId == coupon.Id && r.CustomerId == customerId && r.Status != CouponRedemptionStatus.Released);
        if (coupon.IsUsedUp ||
            (coupon.UsageLimitPerCustomer.HasValue && customerUses >= coupon.UsageLimitPerCustomer.Value))
        {
            throw new BusinessException(SahamErrorCodes.CouponUsageLimitReached);
        }

        // ExecuteUpdate bypasses the change tracker; a new stamp makes any older tracked Coupon fail
        // its ConcurrencyStamp check instead of writing back a stale UsedCount.
        var changed = await db.Coupons
            .Where(c => c.Id == coupon.Id && c.Status == CouponStatus.Active && !c.IsDeleted &&
                        (c.UsageLimitTotal == null || c.UsedCount < c.UsageLimitTotal))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.UsedCount, c => c.UsedCount + 1)
                .SetProperty(c => c.ConcurrencyStamp, NewStamp()));
        if (changed != 1)
        {
            throw new BusinessException(SahamErrorCodes.CouponUsageLimitReached);
        }

        await db.CouponRedemptions.AddAsync(new CouponRedemption(GuidGenerator.Create(), coupon.Id, orderId, customerId, expected.Amount));
        // A flush, not a commit: the unique OrderId index runs now; any error aborts the whole unit of work.
        await db.SaveChangesAsync();
    }

    /* Delivery / "not received" / late customer cancellation: the use stays taken. */
    public Task ConsumeAsync(Guid orderId) => FinishAsync(orderId, release: false);

    /* Only where the cancellation / payment policy gives the use back (LedgerOrderEventHandler decides). */
    public Task ReleaseAsync(Guid orderId) => FinishAsync(orderId, release: true);

    /* BR-048: an item removal can lower K, never raise it. */
    public async Task UpdateAmountAsync(Guid orderId, long amount)
    {
        RequireId(orderId, nameof(orderId));
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        var db = await GetTransactionalContextAsync();
        var couponId = await FindCouponIdAsync(db, orderId);
        if (!couponId.HasValue)
        {
            return;
        }

        await LockCouponAsync(db, couponId.Value);
        var redemption = await db.CouponRedemptions.AsNoTracking().SingleAsync(r => r.OrderId == orderId);
        if (redemption.Amount == amount)
        {
            return;
        }

        if (redemption.Status != CouponRedemptionStatus.Reserved)
        {
            throw new BusinessException(SahamErrorCodes.CouponReservationNotReserved);
        }

        if (amount > redemption.Amount)
        {
            throw new BusinessException(SahamErrorCodes.CouponDiscountCannotIncrease);
        }

        var changed = await db.CouponRedemptions
            .Where(r => r.Id == redemption.Id && r.Status == CouponRedemptionStatus.Reserved && r.Amount == redemption.Amount)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Amount, amount).SetProperty(r => r.ConcurrencyStamp, NewStamp()));
        if (changed != 1)
        {
            throw new InvalidOperationException("Coupon reservation changed unexpectedly.");
        }
    }

    private async Task FinishAsync(Guid orderId, bool release)
    {
        RequireId(orderId, nameof(orderId));
        var db = await GetTransactionalContextAsync();
        var couponId = await FindCouponIdAsync(db, orderId);
        if (!couponId.HasValue)
        {
            return; // the order used no coupon
        }

        // Coupon before redemption, like every writer. A disabled or deleted coupon still finishes its uses.
        await LockCouponAsync(db, couponId.Value);
        var next = release ? CouponRedemptionStatus.Released : CouponRedemptionStatus.Consumed;
        var changed = await db.CouponRedemptions
            .Where(r => r.OrderId == orderId && r.CouponId == couponId.Value && r.Status == CouponRedemptionStatus.Reserved)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, next).SetProperty(r => r.ConcurrencyStamp, NewStamp()));
        if (changed == 0)
        {
            return; // a replay, or already final: never give the use back twice
        }

        if (release)
        {
            var restored = await db.Coupons.IgnoreQueryFilters()
                .Where(c => c.Id == couponId.Value && c.UsedCount > 0)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.UsedCount, c => c.UsedCount - 1)
                    .SetProperty(c => c.ConcurrencyStamp, NewStamp()));
            if (restored != 1)
            {
                throw new InvalidOperationException("Coupon counter is inconsistent; roll back and reconcile.");
            }
        }
    }

    private static Task<Guid?> FindCouponIdAsync(SahamDbContext db, Guid orderId) =>
        db.CouponRedemptions.AsNoTracking().Where(r => r.OrderId == orderId).Select(r => (Guid?)r.CouponId).SingleOrDefaultAsync();

    private async Task<SahamDbContext> GetTransactionalContextAsync()
    {
        var db = await _contextProvider.GetDbContextAsync();
        var transaction = db.Database.CurrentTransaction;
        // ABP opens the transaction without a level: PostgreSQL then runs READ COMMITTED (its default).
        var level = transaction?.GetDbTransaction().IsolationLevel;
        if (level is not (IsolationLevel.ReadCommitted or IsolationLevel.Unspecified))
        {
            throw new InvalidOperationException("Coupon reservation needs the caller's READ COMMITTED transaction.");
        }

        if (db.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            throw new InvalidOperationException("Coupon reservation needs PostgreSQL.");
        }

        return db;
    }

    private async Task<Coupon> LockCouponAsync(SahamDbContext db, Guid couponId)
    {
        // Bounded wait (the same setting as the customer lock, below the 30 s command timeout): a long
        // holder becomes a 409 "try again" and the whole request rolls back, never a 500.
        var timeoutSeconds = await _settingProvider.GetAsync<int>(SahamSettings.Orders.BalanceLockTimeoutSeconds);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT set_config('lock_timeout', {0}, true)", $"{Math.Max(1, timeoutSeconds)}s");
            // Table name of the current mapping (prefix App, no schema). Filters ignored so a deleted coupon's
            // uses can still finish; AsNoTracking so an older tracked copy is never returned.
            var rows = await db.Coupons
                .FromSqlInterpolated($"SELECT * FROM \"AppCoupons\" WHERE \"Id\" = {couponId} FOR UPDATE")
                .IgnoreQueryFilters().AsNoTracking().ToListAsync();
            await db.Database.ExecuteSqlRawAsync("SELECT set_config('lock_timeout', '0', true)");
            return rows.SingleOrDefault() ?? throw new BusinessException(SahamErrorCodes.CouponNotFound);
        }
        catch (Exception ex) when (CustomerBalanceLock.IsLockTimeout(ex))
        {
            throw new BusinessException(SahamErrorCodes.CouponBusy);
        }
    }

    private string NewStamp() => GuidGenerator.Create().ToString("N");

    private static void RequireId(Guid id, string name)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An id is required.", name);
        }
    }
}
