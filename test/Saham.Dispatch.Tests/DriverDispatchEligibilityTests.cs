using System.Reflection;
using Saham;
using Saham.Entities.Dispatch;
using Saham.Entities.Drivers;
using Saham.Entities.Shared;
using Volo.Abp;
using Xunit;

namespace Saham.Dispatch.Tests;

/* Business acceptance cases against the production policy. These tests need no DB,
 * secrets, application startup or workers. PostgreSQL/UoW races require separate
 * integration tests; this suite does not claim to establish their safety. */
public class DriverDispatchEligibilityTests
{
    private static readonly Guid City = Guid.Parse("c21e8937-965a-4904-bc58-f7ccf74c149f");
    private static readonly DateTime Now = new(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Approved_available_idle_driver_is_eligible(bool isCash)
    {
        DriverDispatchEligibility.Ensure(EligibleDriver(), City, isCash, Now, 120, 2, true);
    }

    [Theory]
    [InlineData(DriverReviewStatus.PendingReview)]
    [InlineData(DriverReviewStatus.DocsAccepted)]
    [InlineData(DriverReviewStatus.Rejected)]
    [InlineData(DriverReviewStatus.Suspended)]
    public void Losing_approval_after_offer_prevents_assignment(DriverReviewStatus status)
    {
        var driver = EligibleDriver();
        Assert.True(AutomaticPolicy(driver));
        Set(driver, nameof(Driver.ReviewStatus), status);
        AssertError(driver, SahamErrorCodes.DriverNotAvailable);
    }

    [Fact]
    public void Going_offline_after_offer_prevents_assignment()
    {
        var driver = EligibleDriver();
        driver.SetAvailability(false);
        AssertError(driver, SahamErrorCodes.DriverNotAvailable);
    }

    [Theory]
    [InlineData(DriverCashBlock.Cap)]
    [InlineData(DriverCashBlock.Overdue)]
    public void Cash_block_prevents_cash_but_allows_wallet(DriverCashBlock block)
    {
        var driver = EligibleDriver();
        driver.BlockCash(block, Now);
        AssertError(driver, SahamErrorCodes.DriverCashBlocked);
        DriverDispatchEligibility.Ensure(driver, City, false, Now, 120, 2, true);
    }

    [Theory]
    [InlineData(-120, true)]
    [InlineData(-121, false)]
    [InlineData(120, true)]
    [InlineData(121, false)]
    public void Location_age_and_clock_skew_boundaries(int seconds, bool eligible)
    {
        var driver = EligibleDriver();
        Set(driver, nameof(Driver.LastLocationAt), Now.AddSeconds(seconds));
        Assert.Equal(eligible, AutomaticPolicy(driver));
    }

    [Fact]
    public void Location_ages_out_between_offer_and_acceptance()
    {
        var driver = EligibleDriver();
        Set(driver, nameof(Driver.LastLocationAt), Now.AddSeconds(-119));
        Assert.True(AutomaticPolicy(driver));
        var error = Assert.Throws<BusinessException>(() =>
            DriverDispatchEligibility.Ensure(driver, City, true, Now.AddSeconds(2), 120, 2, true));
        Assert.Equal(SahamErrorCodes.DriverNotAvailable, error.Code);
    }

    [Fact]
    public void Missing_location_or_timestamp_prevents_assignment()
    {
        var driver = EligibleDriver();
        Set(driver, nameof(Driver.LastLocation), null);
        AssertError(driver, SahamErrorCodes.DriverNotAvailable);
        driver = EligibleDriver();
        Set(driver, nameof(Driver.LastLocationAt), null);
        AssertError(driver, SahamErrorCodes.DriverNotAvailable);
    }

    [Fact]
    public void Driver_from_another_city_is_rejected()
    {
        var driver = EligibleDriver();
        Set(driver, nameof(Driver.CityId), Guid.NewGuid());
        AssertError(driver, SahamErrorCodes.DriverNotAvailable);
    }

    [Fact]
    public void Second_task_is_manual_only_and_third_is_always_rejected()
    {
        var driver = EligibleDriver();
        Set(driver, nameof(Driver.ActiveOrderCount), 1);
        AssertError(driver, SahamErrorCodes.DriverAtCapacity);
        DriverDispatchEligibility.Ensure(driver, City, true, Now, 120, 2, false);
        Set(driver, nameof(Driver.ActiveOrderCount), 2);
        AssertError(driver, SahamErrorCodes.DriverAtCapacity, automatic: false);
    }

    [Fact]
    public void Higher_setting_cannot_raise_hard_capacity_above_two()
    {
        var driver = EligibleDriver();
        Set(driver, nameof(Driver.ActiveOrderCount), 2);
        var error = Assert.Throws<BusinessException>(() =>
            DriverDispatchEligibility.Ensure(driver, City, false, Now, 120, 99, false));
        Assert.Equal(SahamErrorCodes.DriverAtCapacity, error.Code);
    }

    [Fact]
    public void Lower_configured_capacity_is_respected()
    {
        var driver = EligibleDriver();
        Set(driver, nameof(Driver.ActiveOrderCount), 1);
        Assert.False(DriverDispatchEligibility.Predicate(City, false, Now, 120, 1, false).Compile()(driver));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(-1, 2)]
    [InlineData(120, 0)]
    public void Invalid_settings_fail_closed(int freshness, int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DriverDispatchEligibility.Predicate(City, true, Now, freshness, capacity, true));
    }

    private static bool AutomaticPolicy(Driver driver) =>
        DriverDispatchEligibility.Predicate(City, true, Now, 120, 2, true).Compile()(driver);

    private static void AssertError(Driver driver, string code, bool automatic = true)
    {
        var error = Assert.Throws<BusinessException>(() =>
            DriverDispatchEligibility.Ensure(driver, City, true, Now, 120, 2, automatic));
        Assert.Equal(code, error.Code);
    }

    private static Driver EligibleDriver()
    {
        // Rehydrate a snapshot like EF does, without opening production constructors
        // or bypassing domain guards in application code.
        var driver = (Driver)Activator.CreateInstance(typeof(Driver), nonPublic: true)!;
        Set(driver, nameof(Driver.CityId), City);
        Set(driver, nameof(Driver.ReviewStatus), DriverReviewStatus.Approved);
        Set(driver, nameof(Driver.Availability), DriverAvailability.Available);
        Set(driver, nameof(Driver.LastLocation), GeoFactory.CreatePoint(32.616, 44.024));
        Set(driver, nameof(Driver.LastLocationAt), Now);
        return driver;
    }

    private static void Set(Driver driver, string property, object? value) =>
        typeof(Driver).GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!.SetValue(driver, value);
}
