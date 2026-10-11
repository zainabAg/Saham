using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Saham.Entities.Customers;
using Saham.Entities.Geography;
using Saham.Entities.Restaurants;
using Saham.Entities.Shared;
using Saham.Settings;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.Settings;

namespace Saham.Entities.Pricing;

/* Builds a quote (UC "POST /quotes", BR-001). Checks, in order:
 *   restaurant active and open now / at the scheduled time   (BR-001, BR-020)
 *   address of this customer, inside the service area        (BR-018)
 *   distance ≤ plan maximum (10 km)                           (BR-007)
 *   every item active and available in this restaurant       (BR-003)
 *   items subtotal ≥ restaurant minimum                       (BR-004)
 * then prices it with the active plan and the customer's balance (BR-064, BR-071). */
public class QuoteManager : DomainService
{
    public const int MaxQuantityPerLine = 99;

    private readonly IRepository<Restaurant, Guid> _restaurantRepository;
    private readonly IRepository<MenuItem, Guid> _menuItemRepository;
    private readonly IRepository<CustomerAddress, Guid> _addressRepository;
    private readonly IRepository<City, Guid> _cityRepository;
    private readonly IRepository<CustomerProfile, Guid> _customerProfileRepository;
    private readonly PricingPlanManager _pricingPlanManager;
    private readonly PricingCalculator _calculator;
    private readonly ICustomerBalanceProvider _balanceProvider;
    private readonly ISettingProvider _settingProvider;
    private readonly Coupons.CouponManager _couponManager;

    public QuoteManager(
        IRepository<Restaurant, Guid> restaurantRepository,
        IRepository<MenuItem, Guid> menuItemRepository,
        IRepository<CustomerAddress, Guid> addressRepository,
        IRepository<City, Guid> cityRepository,
        IRepository<CustomerProfile, Guid> customerProfileRepository,
        PricingPlanManager pricingPlanManager,
        PricingCalculator calculator,
        ICustomerBalanceProvider balanceProvider,
        ISettingProvider settingProvider,
        Coupons.CouponManager couponManager)
    {
        _couponManager = couponManager;
        _restaurantRepository = restaurantRepository;
        _menuItemRepository = menuItemRepository;
        _addressRepository = addressRepository;
        _cityRepository = cityRepository;
        _customerProfileRepository = customerProfileRepository;
        _pricingPlanManager = pricingPlanManager;
        _calculator = calculator;
        _balanceProvider = balanceProvider;
        _settingProvider = settingProvider;
    }

    public async Task<Quote> CreateAsync(
        Guid customerId,
        Guid restaurantId,
        Guid addressId,
        IReadOnlyCollection<(Guid MenuItemId, int Quantity)> requestedLines,
        OrderType orderType,
        DateTime? scheduledFor,
        string? couponCode = null)
    {
        var now = Clock.Now;
        var lines = MergeLines(requestedLines);

        // Restaurant and opening time
        var restaurant = await _restaurantRepository.GetAsync(restaurantId);
        if (restaurant.Status != RestaurantStatus.Active)
        {
            throw new EntityNotFoundException(typeof(Restaurant), restaurantId);
        }

        var city = await _cityRepository.GetAsync(restaurant.CityId);
        await CheckOpeningAsync(restaurant, city, orderType, scheduledFor, now);

        // Address (another customer's address is "not found")
        var address = await _addressRepository.GetAsync(addressId);
        if (address.UserId != customerId)
        {
            throw new EntityNotFoundException(typeof(CustomerAddress), addressId);
        }

        if (address.CityId != city.Id || !city.Serves(address.Location))
        {
            throw new BusinessException(SahamErrorCodes.AddressOutsideServiceArea);
        }

        // Distance in whole meters, no rounding to hundreds (BR-005)
        var plan = await _pricingPlanManager.GetActiveAsync(city.Id);
        var distanceM = (int)Math.Round(GeoFactory.DistanceMeters(restaurant.Location, address.Location));
        if (distanceM > plan.MaxDeliveryDistanceM)
        {
            throw new BusinessException(SahamErrorCodes.AddressOutOfRange)
                .WithData("maxKm", plan.MaxDeliveryDistanceM / 1000);
        }

        // Items
        var itemIds = lines.Select(l => l.MenuItemId).ToList();
        var items = (await _menuItemRepository.GetListAsync(i => itemIds.Contains(i.Id))).ToDictionary(i => i.Id);
        var lineSnapshots = new List<QuoteLineSnapshot>();
        foreach (var (menuItemId, quantity) in lines)
        {
            if (!items.TryGetValue(menuItemId, out var item) || item.RestaurantId != restaurantId || !item.IsOrderable)
            {
                throw new BusinessException(SahamErrorCodes.ItemUnavailable).WithData("menuItemId", menuItemId);
            }

            lineSnapshots.Add(new QuoteLineSnapshot(
                item.Id,
                new QuoteText(item.Name.Ar, item.Name.En, item.Name.Fa, item.Name.Ur),
                item.Price,
                quantity,
                item.Price * quantity));
        }

        var itemsSubtotal = lineSnapshots.Sum(l => l.LineTotal);
        if (itemsSubtotal < restaurant.MinOrderAmount)
        {
            throw new BusinessException(SahamErrorCodes.BelowMinOrder)
                .WithData("minOrder", restaurant.MinOrderAmount)
                .WithData("missing", restaurant.MinOrderAmount - itemsSubtotal);
        }

        // Coupon (BR-010/011): one per order, on the items only, funded by the company or the restaurant (BR-012).
        QuoteCouponSnapshot? couponSnapshot = null;
        if (!couponCode.IsNullOrWhiteSpace())
        {
            var coupon = await _couponManager.FindValidAsync(couponCode!, customerId, restaurantId, itemsSubtotal, now);
            couponSnapshot = new QuoteCouponSnapshot(coupon.Id, coupon.Code, coupon.DiscountType, coupon.Value,
                coupon.MaxDiscount, coupon.FundedBy, coupon.StackableWithFreeDelivery, coupon.DiscountFor(itemsSubtotal));
        }

        // Amounts
        var couponAmount = couponSnapshot?.Amount ?? 0;
        var breakdown = _calculator.Calculate(
            plan,
            distanceM,
            lineSnapshots.Select(l => (l.UnitPrice, l.Quantity)).ToList(),
            restaurant.CommissionRateBp,
            companyDiscount: couponSnapshot?.FundedBy == Coupons.CouponFundedBy.Company ? couponAmount : 0,
            restaurantDiscount: couponSnapshot?.FundedBy == Coupons.CouponFundedBy.Restaurant ? couponAmount : 0,
            couponBlocksFreeDelivery: couponSnapshot is { StackableWithFreeDelivery: false });

        var balance = await _balanceProvider.GetBalanceAsync(customerId);
        var (debt, credit, amountDue) = _calculator.ApplyBalance(breakdown.TotalAmount, balance);
        var profile = await _customerProfileRepository.FindAsync(customerId);

        var (etaMin, etaMax) = await EstimateArrivalAsync(restaurant, distanceM, orderType, scheduledFor, now);

        var snapshot = new QuoteSnapshot(
            restaurant.Id,
            plan.Id,
            new QuoteAddressSnapshot(
                address.Id,
                address.Location.Y,
                address.Location.X,
                address.Description,
                address.Label == AddressLabel.Other && address.CustomLabel != null ? address.CustomLabel : address.Label.ToString(),
                address.RecipientName,
                address.RecipientPhone),
            lineSnapshots,
            breakdown,
            debt,
            credit,
            amountDue,
            CashAllowed: profile is not { CashDisabled: true },
            etaMin,
            etaMax,
            couponSnapshot);

        var lifetime = await _settingProvider.GetAsync<int>(SahamSettings.Orders.QuoteLifetimeMinutes);
        return new Quote(GuidGenerator.Create(), customerId, orderType, scheduledFor, snapshot, now.AddMinutes(lifetime));
    }

    private async Task CheckOpeningAsync(Restaurant restaurant, City city, OrderType orderType, DateTime? scheduledFor, DateTime now)
    {
        if (orderType == OrderType.Asap)
        {
            if (!restaurant.AcceptsOrdersAt(now, city.ToLocalTime(now)))
            {
                var next = restaurant.GetNextOpeningAt(city.ToLocalTime(now));
                throw new BusinessException(SahamErrorCodes.RestaurantClosed)
                    .WithData("nextOpeningAt", next.HasValue ? city.ToUtc(next.Value).ToString("O") : "-");
            }

            return;
        }

        // BR-020: at least N minutes ahead, at most N days ahead, within the restaurant's hours.
        var minLead = await _settingProvider.GetAsync<int>(SahamSettings.Orders.ScheduleMinLeadMinutes);
        var maxDays = await _settingProvider.GetAsync<int>(SahamSettings.Orders.ScheduleMaxDays);
        if (!scheduledFor.HasValue ||
            scheduledFor.Value < now.AddMinutes(minLead) ||
            scheduledFor.Value > now.AddDays(maxDays) ||
            !restaurant.IsOpenAt(city.ToLocalTime(scheduledFor.Value)))
        {
            throw new BusinessException(SahamErrorCodes.InvalidScheduleTime)
                .WithData("minLeadMinutes", minLead)
                .WithData("maxDays", maxDays);
        }
    }

    /* Shown as a range (e.g. 35–45 min, REQ-CUS-03): preparation + pickup buffer + travel time
     * at an average speed; the upper bound is the "late" threshold (ASM-21). */
    private async Task<(DateTime Min, DateTime Max)> EstimateArrivalAsync(
        Restaurant restaurant, int distanceM, OrderType orderType, DateTime? scheduledFor, DateTime now)
    {
        var range = await _settingProvider.GetAsync<int>(SahamSettings.Orders.LateThresholdMinutes);
        if (orderType == OrderType.Scheduled && scheduledFor.HasValue)
        {
            return (scheduledFor.Value, scheduledFor.Value.AddMinutes(range));
        }

        var min = now.AddMinutes(restaurant.DefaultPrepMinutes + await GetTravelMinutesAsync(distanceM));
        return (min, min.AddMinutes(range));
    }

    /* Minutes from "ready" to the customer's door: driver reaching the restaurant + the ride. */
    public async Task<int> GetTravelMinutesAsync(int distanceM)
    {
        var speedKmh = await _settingProvider.GetAsync<int>(SahamSettings.Orders.AverageSpeedKmh);
        var buffer = await _settingProvider.GetAsync<int>(SahamSettings.Orders.PickupBufferMinutes);
        return buffer + (int)Math.Ceiling(distanceM / 1000d / Math.Max(1, speedKmh) * 60);
    }

    /* Same item twice in the cart = one line with the summed quantity. */
    private static List<(Guid MenuItemId, int Quantity)> MergeLines(IReadOnlyCollection<(Guid MenuItemId, int Quantity)> lines)
    {
        if (lines.Count == 0)
        {
            throw new BusinessException(SahamErrorCodes.EmptyCart);
        }

        var merged = lines
            .GroupBy(l => l.MenuItemId)
            .Select(g => (g.Key, g.Sum(l => l.Quantity)))
            .ToList();

        if (merged.Any(l => l.Item2 < 1 || l.Item2 > MaxQuantityPerLine))
        {
            throw new BusinessException(SahamErrorCodes.InvalidQuantity).WithData("max", MaxQuantityPerLine);
        }

        return merged;
    }
}
