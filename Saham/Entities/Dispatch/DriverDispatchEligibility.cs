using System;
using System.Linq.Expressions;
using Saham.Entities.Drivers;
using Volo.Abp;

namespace Saham.Entities.Dispatch;

/* BR-026/030/066/067: one eligibility predicate for database selection and final
 * assignment validation. Automatic dispatch only takes idle drivers (MVP TD-06);
 * operations may assign a second task. Capacity never exceeds the DB ceiling of 2.
 * The two-minute future tolerance matches DriverLocationAppService's clock-skew rule. */
public static class DriverDispatchEligibility
{
    public static Expression<Func<Driver, bool>> Predicate(
        Guid cityId, bool isCash, DateTime now, int freshSeconds, int maxActiveOrders,
        bool automatic)
    {
        if (freshSeconds <= 0 || maxActiveOrders <= 0)
        {
            throw new ArgumentOutOfRangeException(
                freshSeconds <= 0 ? nameof(freshSeconds) : nameof(maxActiveOrders));
        }

        var oldestLocation = now.AddSeconds(-freshSeconds);
        var latestLocation = now.AddMinutes(2);
        var capacity = automatic ? 1 : Math.Min(2, maxActiveOrders);

        return d => d.CityId == cityId
                    && d.ReviewStatus == DriverReviewStatus.Approved
                    && d.Availability == DriverAvailability.Available
                    && d.LastLocation != null
                    && d.LastLocationAt >= oldestLocation
                    && d.LastLocationAt <= latestLocation
                    && d.ActiveOrderCount >= 0
                    && d.ActiveOrderCount < capacity
                    && (!isCash || d.CashBlocked == null);
    }

    public static void Ensure(
        Driver driver, Guid cityId, bool isCash, DateTime now, int freshSeconds,
        int maxActiveOrders, bool automatic)
    {
        if (Predicate(cityId, isCash, now, freshSeconds, maxActiveOrders, automatic)
            .Compile()(driver))
        {
            return;
        }

        if (isCash && driver.CashBlocked != null)
        {
            throw new BusinessException(SahamErrorCodes.DriverCashBlocked);
        }

        var capacity = automatic ? 1 : Math.Min(2, maxActiveOrders);
        if (driver.ActiveOrderCount >= capacity)
        {
            throw new BusinessException(SahamErrorCodes.DriverAtCapacity);
        }

        throw new BusinessException(SahamErrorCodes.DriverNotAvailable);
    }
}
