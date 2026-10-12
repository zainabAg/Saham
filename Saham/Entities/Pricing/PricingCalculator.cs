using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Saham.Entities.Pricing;

/* The order amounts of SRS 12.3 (all approved by the client, DEC-03..DEC-08), in whole IQD:
 *   S    = Σ unit price × quantity                      (items subtotal)
 *   K    = K_c + K_r                                    (company- / restaurant-funded discount)
 *   Dfee = tier fee, or 0 when free delivery applies    (BR-006, BR-008)
 *   T    = S − K + Dfee                                 (BR-013)
 *   C    = round((S − K_r) × rate_bp / 10000)           (BR-015, half-up)
 *   R    = S − K_r − C                                  (BR-016)
 *   P    = base pay + tier extra                        (BR-017; batched second order = fixed pay)
 * The server alone computes these; amounts sent by a client are ignored. */
public class PricingCalculator : ISingletonDependency
{
    public PriceBreakdown Calculate(
        PricingPlan plan,
        int distanceM,
        IReadOnlyCollection<(long UnitPrice, int Quantity)> lines,
        int commissionRateBp,
        long companyDiscount = 0,
        long restaurantDiscount = 0,
        bool couponBlocksFreeDelivery = false)
    {
        var tier = plan.FindTier(distanceM)
                   ?? throw new BusinessException(SahamErrorCodes.AddressOutOfRange)
                       .WithData("maxKm", plan.MaxDeliveryDistanceM / 1000);

        var itemsSubtotal = lines.Sum(l => l.UnitPrice * l.Quantity);
        var discount = companyDiscount + restaurantDiscount;
        if (companyDiscount < 0 || restaurantDiscount < 0 || discount > itemsSubtotal)
        {
            throw new BusinessException(SahamErrorCodes.InvalidAmount);
        }

        // BR-008: free delivery above the threshold, near and middle bands only.
        var freeDeliveryBase = plan.FreeDeliveryBasis == FreeDeliveryBasis.AfterDiscount
            ? itemsSubtotal - discount
            : itemsSubtotal;
        var aboveThreshold = plan.FreeDeliveryOperator == FreeDeliveryOperator.GreaterThan
            ? freeDeliveryBase > plan.FreeDeliveryThreshold
            : freeDeliveryBase >= plan.FreeDeliveryThreshold;
        var freeDeliveryApplied = tier.FreeDeliveryEligible && aboveThreshold && !couponBlocksFreeDelivery;

        // "Add X more for free delivery" (REQ-CUS-09), only where free delivery is possible.
        long? freeDeliveryRemaining = null;
        if (tier.FreeDeliveryEligible && !freeDeliveryApplied && !couponBlocksFreeDelivery)
        {
            var target = plan.FreeDeliveryOperator == FreeDeliveryOperator.GreaterThan
                ? plan.FreeDeliveryThreshold + 1
                : plan.FreeDeliveryThreshold;
            freeDeliveryRemaining = target - freeDeliveryBase;
        }

        var deliveryFeeBase = tier.CustomerFee;
        var deliveryFee = freeDeliveryApplied ? 0 : deliveryFeeBase;
        var total = itemsSubtotal - discount + deliveryFee;

        var commission = RoundHalfUp((itemsSubtotal - restaurantDiscount) * commissionRateBp, 10_000);
        var restaurantNet = itemsSubtotal - restaurantDiscount - commission;
        var driverPay = plan.DriverBasePay + tier.DriverExtra;

        return new PriceBreakdown(
            distanceM,
            tier.FromM,
            tier.ToM,
            itemsSubtotal,
            discount,
            companyDiscount,
            restaurantDiscount,
            deliveryFeeBase,
            freeDeliveryApplied,
            deliveryFee,
            total,
            commissionRateBp,
            commission,
            restaurantNet,
            driverPay,
            freeDeliveryRemaining);
    }

    /* ASM-28 / BR-062: half-up rounding to the nearest dinar, for non-negative amounts. */
    public static long RoundHalfUp(long numerator, long denominator)
    {
        if (numerator < 0 || denominator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numerator));
        }

        return (numerator + denominator / 2) / denominator;
    }
}

public record PriceBreakdown(
    int DistanceM,
    int TierFromM,
    int TierToM,
    long ItemsSubtotal,
    long DiscountAmount,
    long CompanyDiscount,
    long RestaurantDiscount,
    long DeliveryFeeBase,
    bool FreeDeliveryApplied,
    long DeliveryFee,
    long TotalAmount,
    int CommissionRateBp,
    long CommissionAmount,
    long RestaurantNetAmount,
    long DriverPayAmount,
    long? FreeDeliveryRemaining);
