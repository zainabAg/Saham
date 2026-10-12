using System.Reflection;
using Saham;
using Saham.Data.Configurations;
using Saham.Entities.Customers;
using Saham.Entities.Orders;
using Volo.Abp;
using Xunit;

namespace Saham.Finance.Tests;

/* The reservation's own rules and the fixed parts of the lock and migration. No DB, no ABP startup:
 * these do NOT show that the lock, the transaction or the database constraints work under load. */
public class CustomerBalanceReservationTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(5_000, 0)]
    [InlineData(0, 3_000)]
    [InlineData(0, 0)]
    public void New_hold_is_active_with_one_kind(long credit, long debt)
    {
        var hold = Create(credit, debt);
        Assert.True(hold.IsActive);
        Assert.Equal(CustomerBalanceReservationSource.OrderCreated, hold.Source);
        Assert.Null(hold.FinalizedAt);
        Assert.Equal(new CustomerBalanceReservationSnapshot(hold.OrderId, credit, debt), hold.ToSnapshot());
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(1, 1)]
    public void Negative_or_mixed_amounts_are_rejected(long credit, long debt)
    {
        var error = Assert.Throws<BusinessException>(() => Create(credit, debt));
        Assert.Equal(SahamErrorCodes.InvalidAmount, error.Code);
    }

    [Fact]
    public void Consume_is_idempotent_and_final()
    {
        var hold = Create(8_000, 0);
        Assert.True(Call<bool>(hold, "Consume", "DELIVERED", Now));
        Assert.False(Call<bool>(hold, "Consume", "DELIVERED", Now.AddMinutes(1))); // replayed event
        Assert.Equal(CustomerBalanceReservationStatus.Consumed, hold.Status);
        Assert.Equal(Now, hold.FinalizedAt);
        Assert.Equal("DELIVERED", hold.FinalizeReason);
        AssertConflict(() => Call<bool>(hold, "Release", "CANCELLED", Now));
    }

    [Fact]
    public void Released_hold_cannot_be_consumed()
    {
        var hold = Create(0, 3_000);
        Assert.True(Call<bool>(hold, "Release", "CANCELLED_PaymentTimeout", Now));
        Assert.False(Call<bool>(hold, "Release", "CANCELLED_PaymentTimeout", Now));
        AssertConflict(() => Call<bool>(hold, "Consume", "DELIVERED", Now));
    }

    [Fact]
    public void Modification_may_only_lower_the_hold()
    {
        var hold = Create(8_000, 0);
        Call<object?>(hold, "AdjustTo", 6_000L, 0L);
        Assert.Equal(6_000, hold.CreditApplied);
        AssertConflict(() => Call<object?>(hold, "AdjustTo", 7_000L, 0L));   // never grows silently
        AssertConflict(() => Call<object?>(hold, "AdjustTo", 0L, 1L));
    }

    [Fact]
    public void Finalized_hold_cannot_be_adjusted()
    {
        var hold = Create(8_000, 0);
        Call<bool>(hold, "Consume", "DELIVERED", Now);
        AssertConflict(() => Call<object?>(hold, "AdjustTo", 1_000L, 0L));
    }

    [Fact]
    public void Every_order_end_reason_fits_the_column()
    {
        var longest = Enum.GetNames<CancelReason>().Select(n => "CANCELLED_" + n)
            .Concat(Enum.GetNames<FailureReason>().Select(n => "FAILED_" + n))
            .Max(r => r.Length);
        Assert.True(longest <= CustomerBalanceReservation.ReasonMaxLength, $"{longest} characters");
    }

    [Fact]
    public void Lock_key_is_stable_and_spreads_customers()
    {
        var id = Guid.Parse("8f0d6b8e-2a4c-4b9e-9a7d-1c2b3d4e5f60");
        Assert.Equal(CustomerBalanceLock.KeyOf(id), CustomerBalanceLock.KeyOf(Guid.Parse(id.ToString())));
        var keys = Enumerable.Range(0, 10_000).Select(_ => CustomerBalanceLock.KeyOf(Guid.NewGuid())).Distinct().Count();
        Assert.True(keys > 9_990); // collisions only serialise two customers; they never break correctness
    }

    [Fact]
    public void Backfill_uses_the_stored_enum_names()
    {
        // Status columns hold enum names (HasConversion<string>); a rename must not silently skip open orders.
        foreach (var status in new[] { OrderStatus.Delivered, OrderStatus.Cancelled, OrderStatus.DeliveryFailed })
        {
            Assert.Contains($"'{status}'", CustomerBalanceReservationSql.BackfillOpenOrders);
        }

        Assert.Contains($"'{CustomerBalanceReservationStatus.Active}'", CustomerBalanceReservationSql.BackfillOpenOrders);
        Assert.Contains($"'{CustomerBalanceReservationSource.LegacyBackfill}'", CustomerBalanceReservationSql.BackfillOpenOrders);
        Assert.Contains("ON CONFLICT (\"OrderId\") DO NOTHING", CustomerBalanceReservationSql.BackfillOpenOrders);
        Assert.Contains($"'{Saham.Entities.Ledger.LedgerAccountCode.CustomerBalance}'", CustomerBalanceReservationSql.ReconciliationQuery);
    }

    private static CustomerBalanceReservation Create(long credit, long debt)
    {
        try
        {
            return (CustomerBalanceReservation)Activator.CreateInstance(typeof(CustomerBalanceReservation),
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), credit, debt }, null)!;
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException!;
        }
    }

    private static T Call<T>(CustomerBalanceReservation hold, string method, params object[] args)
    {
        try
        {
            return (T)typeof(CustomerBalanceReservation)
                .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(hold, args)!;
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException!;
        }
    }

    private static void AssertConflict(Action action)
    {
        var error = Assert.Throws<BusinessException>(action);
        Assert.Equal(SahamErrorCodes.CustomerBalanceReservationConflict, error.Code);
    }
}
