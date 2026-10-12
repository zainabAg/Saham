using System;
using System.Linq;
using System.Threading.Tasks;
using Saham.Entities.Customers;
using Saham.Entities.Pricing;
using Saham.Entities.Restaurants;
using Saham.Identity;
using Saham.Settings;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Settings;
using Volo.Abp.Uow;

namespace Saham.Entities.Orders;

/* Creates orders from quotes (BR-001 steps 4–6, DER-CUS-06):
 *   - the quote belongs to the customer, is not expired and not used yet;
 *   - everything is re-validated and re-priced now; if anything differs from what the
 *     customer saw, a fresh quote is saved and PRICE_CHANGED is returned (SRS 6-A);
 *   - cash only when allowed (BR-065), and the banknote must cover the amount (UC A4);
 *   - a wallet order waits in PendingPayment until the provider captures it (UC-05, PaymentManager);
 *   - the coupon is reserved in the same transaction (BR-010), so its last use is taken once;
 *   - everything runs under the customer's balance lock, taken before the quote and the balance are
 *     read: two orders of one customer cannot spend the same credit or collect the same debt. The
 *     caller saves the order and its reservation (CustomerBalanceReservationManager) in this transaction. */
public class OrderManager : DomainService
{
    private readonly IRepository<Quote, Guid> _quoteRepository;
    private readonly IRepository<Restaurant, Guid> _restaurantRepository;
    private readonly QuoteManager _quoteManager;
    private readonly ISettingProvider _settingProvider;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly Coupons.CouponManager _couponManager;
    private readonly CustomerBalanceLock _balanceLock;

    public OrderManager(
        IRepository<Quote, Guid> quoteRepository,
        IRepository<Restaurant, Guid> restaurantRepository,
        QuoteManager quoteManager,
        ISettingProvider settingProvider,
        IUnitOfWorkManager unitOfWorkManager,
        Coupons.CouponManager couponManager,
        CustomerBalanceLock balanceLock)
    {
        _balanceLock = balanceLock;
        _couponManager = couponManager;
        _unitOfWorkManager = unitOfWorkManager;
        _quoteRepository = quoteRepository;
        _restaurantRepository = restaurantRepository;
        _quoteManager = quoteManager;
        _settingProvider = settingProvider;
    }

    public async Task<Order> CreateFromQuoteAsync(
        Guid customerId,
        Guid quoteId,
        PaymentMethod paymentMethod,
        long? cashTendered,
        string? recipientName,
        string? recipientPhone)
    {
        // First: every read below must see what the previous holder of this lock committed.
        await _balanceLock.AcquireAsync(customerId);
        var now = Clock.Now;

        var quote = await _quoteRepository.GetAsync(quoteId);
        // Then the coupon row (lock order: customer → coupon), before re-pricing: the fresh quote below
        // reads terms and uses that cannot change until commit (BR-010).
        if (quote.Snapshot.Coupon != null)
        {
            await _couponManager.LockForOrderAsync(quote.Snapshot.Coupon.CouponId);
        }

        if (quote.CustomerId != customerId)
        {
            throw new EntityNotFoundException(typeof(Quote), quoteId);
        }

        if (quote.IsConsumed)
        {
            throw new BusinessException(SahamErrorCodes.QuoteAlreadyUsed);
        }

        if (quote.IsExpired(now))
        {
            throw new BusinessException(SahamErrorCodes.QuoteExpired);
        }

        if (paymentMethod == PaymentMethod.Wallet && !await _settingProvider.GetAsync<bool>(SahamSettings.Payments.WalletEnabled))
        {
            throw new BusinessException(SahamErrorCodes.WalletNotAvailable);
        }

        // Re-validate and re-price with today's data: restaurant open, items available, prices, plan,
        // balance net of the other orders' holds, cash ban.
        var fresh = await _quoteManager.CreateAsync(
            customerId,
            quote.RestaurantId,
            quote.AddressId,
            quote.Snapshot.Lines.Select(l => (l.MenuItemId, l.Quantity)).ToList(),
            quote.OrderType,
            quote.ScheduledFor,
            quote.Snapshot.Coupon?.Code);

        // The hash covers DebtCollected, CreditApplied and AmountDue; compared here as well so a change of
        // what the hash includes can never freeze money the customer did not agree to.
        if (fresh.PayloadHash != quote.PayloadHash ||
            fresh.Snapshot.DebtCollected != quote.Snapshot.DebtCollected ||
            fresh.Snapshot.CreditApplied != quote.Snapshot.CreditApplied ||
            fresh.Snapshot.AmountDue != quote.Snapshot.AmountDue)
        {
            // Saved in its own transaction: the error below rolls back the order transaction.
            // That transaction only inserts a quote; it never asks for the balance lock held here.
            using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
            {
                await _quoteRepository.InsertAsync(fresh);
                await uow.CompleteAsync();
            }

            throw new BusinessException(SahamErrorCodes.PriceChanged)
                .WithData("newQuoteId", fresh.Id)
                .WithData("newAmountDue", fresh.AmountDue);
        }

        if (paymentMethod == PaymentMethod.Cash && !fresh.Snapshot.CashAllowed) // BR-065: the ban as of now
        {
            throw new BusinessException(SahamErrorCodes.CashNotAllowed);
        }

        if (paymentMethod == PaymentMethod.Cash && cashTendered.HasValue && cashTendered.Value < quote.AmountDue)
        {
            throw new BusinessException(SahamErrorCodes.CashTenderedTooLow).WithData("amountDue", quote.AmountDue);
        }

        var restaurant = await _restaurantRepository.GetAsync(quote.RestaurantId, includeDetails: false);
        var travelMinutes = await _quoteManager.GetTravelMinutesAsync(quote.Snapshot.Breakdown.DistanceM);

        var order = new Order(
            GuidGenerator.Create(),
            restaurant.CityId,
            quote,
            paymentMethod,
            paymentMethod == PaymentMethod.Cash ? cashTendered : null,
            recipientName.IsNullOrWhiteSpace() ? null : recipientName!.Trim(),
            recipientPhone.IsNullOrWhiteSpace() ? null : PhoneNumberNormalizer.Normalize(recipientPhone),
            travelMinutes,
            GuidGenerator.Create);

        // BR-021: a scheduled order reaches the restaurant early enough to prepare and deliver on time.
        DateTime? releaseAt = null;
        if (quote.OrderType == OrderType.Scheduled && quote.ScheduledFor.HasValue)
        {
            var margin = await _settingProvider.GetAsync<int>(SahamSettings.Orders.ScheduleReleaseMarginMinutes);
            releaseAt = quote.ScheduledFor.Value.AddMinutes(-(restaurant.DefaultPrepMinutes + travelMinutes + margin));
            if (releaseAt < now)
            {
                releaseAt = now;
            }
        }

        order.Place(now, releaseAt, GuidGenerator.Create, customerId);
        if (quote.Snapshot.Coupon is { } couponSnapshot)
        {
            // Server values: K and S before the discount, from the quote that matched today's prices.
            await _couponManager.ReserveAsync(couponSnapshot, order.Id, customerId, quote.RestaurantId,
                quote.Snapshot.Lines.Sum(l => l.LineTotal));
        }

        quote.MarkConsumed(order.Id);
        await _quoteRepository.UpdateAsync(quote);

        return order;
    }
}
