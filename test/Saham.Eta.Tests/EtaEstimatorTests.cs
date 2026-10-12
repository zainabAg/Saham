using System.Reflection;
using Saham;
using Saham.Entities.Orders;
using Saham.Entities.Pricing;
using Volo.Abp;
using Xunit;

namespace Saham.Eta.Tests;

/* EtaEstimator and the ETA update in Order.Accept. No DB or ABP startup; the quote, worker and
 * restart paths are covered by the integration run against saham_test. */
public class EtaEstimatorTests
{
    private static readonly DateTime Noon = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
    // Reference: speed 20 km/h, pickup buffer 5, window 10, late grace 10.
    private static readonly EtaEstimator.Policy Policy = new(20, 5, 10, 10);

    [Fact]
    public void Reference_example_asap()
    {
        var e = EtaEstimator.ForQuote(Noon, prepMinutes: 20, distanceMeters: 6_000, Policy);
        Assert.Equal(18, e.TravelMinutes);
        Assert.Equal(Noon.AddMinutes(43), e.MinAt);   // 12:43
        Assert.Equal(Noon.AddMinutes(53), e.MaxAt);   // 12:53
        Assert.Equal(Noon.AddMinutes(63), e.LateAfter); // late only after 13:03
    }

    [Fact]
    public void Late_is_strictly_after_eta_max_plus_grace()
    {
        var max = Noon.AddMinutes(53);
        Assert.False(EtaEstimator.IsLate(Noon.AddMinutes(63), max, 10, terminal: false));          // 13:03 exactly
        Assert.True(EtaEstimator.IsLate(Noon.AddMinutes(63).AddTicks(1), max, 10, terminal: false));
        Assert.True(EtaEstimator.IsLate(Noon.AddMinutes(64), max, 10, terminal: false));
    }

    [Fact]
    public void Finished_orders_are_never_late()
    {
        Assert.False(EtaEstimator.IsLate(Noon.AddHours(5), Noon, 10, terminal: true));
    }

    [Fact]
    public void Window_width_is_independent_of_the_late_grace()
    {
        var e = EtaEstimator.ForQuote(Noon, 20, 6_000, new EtaEstimator.Policy(20, 5, WindowMinutes: 10, LateGraceMinutes: 25));
        Assert.Equal(TimeSpan.FromMinutes(10), e.MaxAt - e.MinAt);
        Assert.Equal(e.MaxAt.AddMinutes(25), e.LateAfter);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]       // 0.003 min rounds up to 1
    [InlineData(333, 1)]     // exactly 0.999 min
    [InlineData(334, 2)]     // 1.002 min
    [InlineData(6_000, 18)]
    [InlineData(10_000, 30)]
    public void Ride_minutes_round_up(int meters, int minutes)
    {
        Assert.Equal(minutes, EtaEstimator.TravelMinutes(meters, 20));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_speed_is_rejected_not_replaced(int speed)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EtaEstimator.TravelMinutes(1_000, speed));
        Assert.Throws<ArgumentOutOfRangeException>(() => EtaEstimator.ForQuote(Noon, 10, 1_000, new EtaEstimator.Policy(speed, 5, 10, 10)));
    }

    [Fact]
    public void Negative_inputs_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EtaEstimator.TravelMinutes(-1, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => EtaEstimator.ForQuote(Noon, -1, 1_000, Policy));
        Assert.Throws<ArgumentOutOfRangeException>(() => EtaEstimator.ForQuote(Noon, 10, 1_000, new EtaEstimator.Policy(20, -1, 10, 10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => EtaEstimator.ForQuote(Noon, 10, 1_000, new EtaEstimator.Policy(20, 5, -1, 10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => EtaEstimator.IsLate(Noon, Noon, -1, false));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Only_utc_times_are_accepted(DateTimeKind kind)
    {
        var t = new DateTime(2026, 10, 12, 12, 0, 0, kind);
        Assert.Throws<ArgumentException>(() => EtaEstimator.ForQuote(t, 10, 1_000, Policy));
        Assert.Throws<ArgumentException>(() => EtaEstimator.ForQuote(Noon, 10, 1_000, Policy, scheduledForUtc: t.AddHours(1)));
        Assert.Throws<ArgumentException>(() => EtaEstimator.IsLate(Noon, t, 10, false));
    }

    [Fact]
    public void Scheduled_time_is_the_arrival_time()
    {
        var at = Noon.AddHours(3);
        var e = EtaEstimator.ForQuote(Noon, 20, 6_000, Policy, at);
        Assert.Equal(at, e.MinAt);
        Assert.Equal(at.AddMinutes(10), e.MaxAt);
    }

    [Fact]
    public void Scheduled_time_in_the_past_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EtaEstimator.ForQuote(Noon, 20, 6_000, Policy, Noon.AddMinutes(-1)));
    }

    [Fact]
    public void Before_pickup_without_a_driver_estimate_adds_the_buffer_once()
    {
        var e = EtaEstimator.BeforePickup(Noon, readyAtUtc: Noon.AddMinutes(20), 6_000, Policy);
        Assert.Equal(Noon.AddMinutes(20 + 5 + 18), e.MinAt);
    }

    [Fact]
    public void Before_pickup_waits_for_the_slower_of_food_and_driver()
    {
        Assert.Equal(Noon.AddMinutes(30 + 18),
            EtaEstimator.BeforePickup(Noon, Noon.AddMinutes(20), 6_000, Policy, driverArrivalAtUtc: Noon.AddMinutes(30)).MinAt);
        Assert.Equal(Noon.AddMinutes(20 + 18),
            EtaEstimator.BeforePickup(Noon, Noon.AddMinutes(20), 6_000, Policy, driverArrivalAtUtc: Noon.AddMinutes(5)).MinAt);
    }

    [Fact]
    public void After_pickup_counts_only_the_remaining_ride()
    {
        Assert.Equal(Noon.AddMinutes(9), EtaEstimator.AfterPickup(Noon, 3_000, Policy).MinAt);
    }

    // ------------------------------------------------------------------ Order.Accept

    [Fact]
    public void Accept_moves_eta_to_ready_plus_buffer_and_ride_once()
    {
        // Quote at 12:00 with default prep 20: TravelMinutes = buffer 5 + ride 18 = 23; window 10.
        var order = PlacedOrder(OrderType.Asap, travelMinutes: 23, etaMin: Noon.AddMinutes(43));
        var acceptedAt = Noon.AddMinutes(2);
        order.Accept(30, 5, 120, Guid.NewGuid(), acceptedAt, Guid.NewGuid);

        Assert.Equal(OrderStatus.Accepted, order.Status);
        Assert.Equal(acceptedAt.AddMinutes(30 + 23), order.EtaMinAt); // 12:55, not 13:00 (buffer once)
        Assert.Equal(order.EtaMinAt.AddMinutes(10), order.EtaMaxAt);
        Assert.Single(order.History);
    }

    [Fact]
    public void Accept_never_moves_a_scheduled_order_before_its_time()
    {
        var scheduled = Noon.AddHours(2);
        var order = PlacedOrder(OrderType.Scheduled, 23, scheduled, scheduled);
        order.Accept(20, 5, 120, Guid.NewGuid(), Noon.AddMinutes(60), Guid.NewGuid); // ready 13:20 → 13:43 < 14:00
        Assert.Equal(scheduled, order.EtaMinAt);
        Assert.Equal(scheduled.AddMinutes(10), order.EtaMaxAt);
    }

    [Fact]
    public void Accept_moves_a_late_scheduled_order_later()
    {
        var scheduled = Noon.AddHours(1);
        var order = PlacedOrder(OrderType.Scheduled, 23, scheduled, scheduled);
        order.Accept(60, 5, 120, Guid.NewGuid(), Noon.AddMinutes(10), Guid.NewGuid); // 12:10 + 60 + 23 = 13:33 > 13:00
        Assert.Equal(Noon.AddMinutes(93), order.EtaMinAt);
    }

    [Fact]
    public void Accept_outside_the_prep_range_changes_nothing()
    {
        var order = PlacedOrder(OrderType.Asap, 23, Noon.AddMinutes(43));
        var error = Assert.Throws<BusinessException>(() => order.Accept(200, 5, 120, Guid.NewGuid(), Noon, Guid.NewGuid));
        Assert.Equal(SahamErrorCodes.InvalidPrepMinutes, error.Code);
        Assert.Equal(Noon.AddMinutes(43), order.EtaMinAt);
        Assert.Equal(OrderStatus.Placed, order.Status);
    }

    private static Order PlacedOrder(OrderType type, int travelMinutes, DateTime etaMin, DateTime? scheduledFor = null)
    {
        var order = (Order)Activator.CreateInstance(typeof(Order), nonPublic: true)!;
        Set(order, nameof(Order.Status), OrderStatus.Placed);
        Set(order, nameof(Order.OrderType), type);
        Set(order, nameof(Order.ScheduledFor), scheduledFor);
        Set(order, nameof(Order.TravelMinutes), travelMinutes);
        Set(order, nameof(Order.EtaMinAt), etaMin);
        Set(order, nameof(Order.EtaMaxAt), etaMin.AddMinutes(10));
        return order;
    }

    private static void Set(Order order, string property, object? value) =>
        typeof(Order).GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!.SetValue(order, value);
}
