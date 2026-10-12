using Saham.Entities.Customers;

// Dependency-free executable tests of the production calculator.
// These prove arithmetic/policy behavior, NOT database concurrency or ABP transactions.
var tests = new (string Name, Action Run)[]
{
    ("No balance", () => Equal(new(0, 0, 12_000, 0), Calc(12_000, 0))),
    ("Partial credit", () => Equal(new(0, 5_000, 7_000, 0), Calc(12_000, 5_000))),
    ("Credit capped at total", () => Equal(new(0, 12_000, 0, 8_000), Calc(12_000, 20_000))),
    ("Exact credit", () => Equal(new(0, 12_000, 0, 0), Calc(12_000, 12_000))),
    ("Unreserved debt", () => Equal(new(3_000, 0, 15_000, 0), Calc(12_000, -3_000))),
    ("Held credit excluded", () => Equal(new(0, 3_000, 9_000, 0), Calc(12_000, 10_000, Hold(7_000, 0)))),
    ("Fully held credit excluded", () => Equal(new(0, 0, 12_000, 0), Calc(12_000, 10_000, Hold(10_000, 0)))),
    ("Held debt excluded", () => Equal(new(1_000, 0, 13_000, 0), Calc(12_000, -3_000, Hold(0, 2_000)))),
    ("Fully held debt excluded", () => Equal(new(0, 0, 12_000, 0), Calc(12_000, -3_000, Hold(0, 3_000)))),
    ("Zero total preserves credit", () => Equal(new(0, 0, 0, 5_000), Calc(0, 5_000))),
    ("Zero total still collects debt", () => Equal(new(3_000, 0, 3_000, 0), Calc(0, -3_000))),
    ("Multiple credit holds", () => Equal(new(0, 4_000, 8_000, 0), Calc(12_000, 10_000, Hold(2_000, 0), Hold(4_000, 0)))),
    ("Reservation order does not matter", () =>
    {
        var a = Hold(2_000, 0); var b = Hold(4_000, 0);
        Equal(Calc(12_000, 10_000, a, b), Calc(12_000, 10_000, b, a));
    }),
    ("Sequential allocations do not double spend credit", () =>
    {
        var first = Calc(8_000, 10_000);
        var second = Calc(8_000, 10_000, Hold(first.CreditApplied, first.DebtCollected));
        Equal(new(0, 2_000, 6_000, 0), second);
        if (first.CreditApplied + second.CreditApplied != 10_000) throw new Exception("Credit allocation mismatch.");
    }),
    ("Sequential allocations do not collect debt twice", () =>
    {
        var first = Calc(8_000, -3_000);
        Equal(new(0, 0, 8_000, 0), Calc(8_000, -3_000, Hold(first.CreditApplied, first.DebtCollected)));
    }),
    ("Released hold makes credit available again", () => Equal(new(0, 8_000, 0, 2_000), Calc(8_000, 10_000))),
    ("Posted hold must be removed with ledger update", () => Equal(new(0, 2_000, 6_000, 0), Calc(8_000, 2_000))),
    ("Overreserved credit rejected", () => Throws<CustomerBalanceReservationConflictException>(() => Calc(1, 5_000, Hold(6_000, 0)))),
    ("Overreserved debt rejected", () => Throws<CustomerBalanceReservationConflictException>(() => Calc(1, -5_000, Hold(0, 6_000)))),
    ("Balance sign changed while credit held", () => Throws<CustomerBalanceReservationConflictException>(() => Calc(1, -1, Hold(1, 0)))),
    ("Duplicate order rejected", () => { var hold = Hold(1, 0); Throws<ArgumentException>(() => Calc(1, 10, hold, hold)); }),
    ("Empty order id rejected", () => Throws<ArgumentException>(() => Calc(1, 0, new CustomerBalanceReservationSnapshot(Guid.Empty, 0, 0)))),
    ("Negative total rejected", () => Throws<ArgumentOutOfRangeException>(() => Calc(-1, 0))),
    ("Negative hold rejected", () => Throws<ArgumentException>(() => Calc(1, 0, Hold(-1, 0)))),
    ("Mixed credit and debt hold rejected", () => Throws<ArgumentException>(() => Calc(1, 0, Hold(1, 1)))),
    ("Null collection rejected", () => Throws<ArgumentNullException>(() => CustomerBalanceReservationCalculator.Calculate(1, 0, null!))),
    ("Null hold rejected", () => Throws<ArgumentNullException>(() => Calc(1, 0, new CustomerBalanceReservationSnapshot[] { null! }))),
    ("MinValue balance rejected", () => Throws<ArgumentOutOfRangeException>(() => Calc(1, long.MinValue))),
    ("Gross due overflow rejected", () => Throws<OverflowException>(() => Calc(long.MaxValue, -1))),
    ("Reservation sum overflow rejected", () => Throws<OverflowException>(() => Calc(1, long.MaxValue, Hold(long.MaxValue, 0), Hold(1, 0)))),
    ("Largest representable total", () => Equal(new(0, 0, long.MaxValue, 0), Calc(long.MaxValue, 0)))
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}: {error.Message}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed.");
return failures == 0 ? 0 : 1;

static CustomerBalanceReservationPlan Calc(long total, long balance, params CustomerBalanceReservationSnapshot[] holds) =>
    CustomerBalanceReservationCalculator.Calculate(total, balance, holds);

static CustomerBalanceReservationSnapshot Hold(long credit, long debt) => new(Guid.NewGuid(), credit, debt);

static void Equal(CustomerBalanceReservationPlan expected, CustomerBalanceReservationPlan actual)
{
    if (expected != actual) throw new Exception($"Expected {expected}; got {actual}.");
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}
