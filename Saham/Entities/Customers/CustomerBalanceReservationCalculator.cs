using System;
using System.Collections.Generic;

namespace Saham.Entities.Customers;

/// <summary>
/// Allocates posted customer credit/debt to one NEW order without allocating
/// money already promised to another order (BR-064 / BR-071).
/// All amounts are whole IQD; no floating-point arithmetic is used.
/// </summary>
/// <remarks>
/// Pure calculation only: this class does NOT acquire a lock or save a hold.
/// The caller MUST read the ledger and active holds, calculate, then persist
/// the hold and order under the SAME customer lock and database transaction.
/// Quotes are previews, not reservations. Never supply balances from a DTO.
/// Complexity: O(n) time and O(n) space for duplicate detection.
/// </remarks>
public static class CustomerBalanceReservationCalculator
{
    /// <param name="orderTotal">T after discounts and delivery, before balance adjustment.</param>
    /// <param name="postedBalance">
    /// Confirmed ledger balance: positive = customer credit, negative = debt.
    /// This is the sign convention of LedgerCustomerBalanceProvider.
    /// </param>
    /// <param name="activeReservations">
    /// Outstanding, unposted holds of OTHER orders belonging to this customer.
    /// Include pending payment and scheduled orders; exclude released and posted holds.
    /// </param>
    public static CustomerBalanceReservationPlan Calculate(
        long orderTotal,
        long postedBalance,
        IReadOnlyCollection<CustomerBalanceReservationSnapshot> activeReservations)
    {
        ArgumentNullException.ThrowIfNull(activeReservations);
        if (orderTotal < 0)
            throw new ArgumentOutOfRangeException(nameof(orderTotal), "Order total cannot be negative.");

        // -long.MinValue is not representable in Int64; reject instead of wrapping.
        if (postedBalance == long.MinValue)
            throw new ArgumentOutOfRangeException(nameof(postedBalance), "Debt exceeds the supported range.");

        long reservedCredit = 0;
        long reservedDebt = 0;
        var orderIds = new HashSet<Guid>();

        foreach (var reservation in activeReservations)
        {
            ArgumentNullException.ThrowIfNull(reservation);
            if (reservation.OrderId == Guid.Empty)
                throw new ArgumentException("A reservation needs an order id.", nameof(activeReservations));

            // A duplicate JOIN row must not silently deduct the same hold twice.
            if (!orderIds.Add(reservation.OrderId))
                throw new ArgumentException("Duplicate order reservation.", nameof(activeReservations));

            if (reservation.CreditApplied < 0 || reservation.DebtCollected < 0)
                throw new ArgumentException("Reserved amounts cannot be negative.", nameof(activeReservations));

            // One signed balance cannot produce both credit and debt at allocation time.
            if (reservation.CreditApplied > 0 && reservation.DebtCollected > 0)
                throw new ArgumentException("A hold cannot reserve both credit and debt.", nameof(activeReservations));

            // Overflow is a data/amount error, never a negative balance or a free order.
            reservedCredit = checked(reservedCredit + reservation.CreditApplied);
            reservedDebt = checked(reservedDebt + reservation.DebtCollected);
        }

        var postedCredit = postedBalance > 0 ? postedBalance : 0;
        var postedDebt = postedBalance < 0 ? -postedBalance : 0;

        // Do not hide inconsistent holds using Math.Max(0, ...).
        // This may indicate a stale read, an adjustment affecting promised funds,
        // or settlement that posted without consuming its hold atomically.
        // Reconcile through the lifecycle service before accepting a new order.
        if (reservedCredit > postedCredit || reservedDebt > postedDebt)
            throw new CustomerBalanceReservationConflictException();

        var availableCredit = postedCredit - reservedCredit;
        var availableDebt = postedDebt - reservedDebt;

        // Reserve all currently unallocated debt for this order (BR-064),
        // and credit only up to the amount owed (BR-071; no cash payout).
        var grossDue = checked(orderTotal + availableDebt);
        var creditApplied = Math.Min(availableCredit, grossDue);
        var amountDue = grossDue - creditApplied;

        return new CustomerBalanceReservationPlan(
            availableDebt, creditApplied, amountDue, availableCredit - creditApplied);
    }
}

/// <summary>
/// Immutable projection of a persisted active hold. It is not an EF entity.
/// Use one hold per order and retain it until its posting/release commits.
/// </summary>
public sealed record CustomerBalanceReservationSnapshot(
    Guid OrderId,
    long CreditApplied,
    long DebtCollected);

/// <summary>
/// Server-generated monetary plan for one order.
/// AmountDue = T + DebtCollected - CreditApplied.
/// RemainingAvailableCredit excludes both existing holds and this new plan.
/// </summary>
public sealed record CustomerBalanceReservationPlan(
    long DebtCollected,
    long CreditApplied,
    long AmountDue,
    long RemainingAvailableCredit);

/// <summary>
/// Fail closed when outstanding promises exceed the matching posted pool.
/// Map to a controlled business error, log and reconcile; do not silently clamp.
/// </summary>
public sealed class CustomerBalanceReservationConflictException : InvalidOperationException
{
    public const string ErrorCode = "Saham:CustomerBalanceReservationConflict";

    public CustomerBalanceReservationConflictException()
        : base("Outstanding customer balance reservations conflict with the posted balance.")
    {
    }
}
