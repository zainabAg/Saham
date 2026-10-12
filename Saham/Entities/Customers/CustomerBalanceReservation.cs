using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace Saham.Entities.Customers;

public enum CustomerBalanceReservationStatus
{
    Active = 0,   // promised to an open order (also PendingPayment and Scheduled)
    Consumed = 1, // the same amounts were posted to CUSTOMER_BALANCE (delivery entry)
    Released = 2  // the order ended without posting them; the money is free again
}

public enum CustomerBalanceReservationSource
{
    OrderCreated = 0,
    LegacyBackfill = 1 // open orders that existed before reservations (Phase10 migration)
}

/* The customer's credit used, or debt collected, by one order, held from creation until the
 * order's ledger entry posts them or the order ends without them (BR-064, BR-071).
 * Not a ledger entry and not revenue: the confirmed balance stays in CUSTOMER_BALANCE.
 * Written only under CustomerBalanceLock (CustomerBalanceReservationManager). */
public class CustomerBalanceReservation : AuditedAggregateRoot<Guid>
{
    public const int ReasonMaxLength = 64;

    public Guid CustomerId { get; private set; }

    public Guid OrderId { get; private set; }

    public long CreditApplied { get; private set; }

    public long DebtCollected { get; private set; }

    public CustomerBalanceReservationStatus Status { get; private set; }

    public CustomerBalanceReservationSource Source { get; private set; }

    public DateTime? FinalizedAt { get; private set; }

    public string? FinalizeReason { get; private set; }

    protected CustomerBalanceReservation()
    {
    }

    internal CustomerBalanceReservation(Guid id, Guid customerId, Guid orderId, long creditApplied, long debtCollected)
        : base(id)
    {
        CustomerId = customerId;
        OrderId = orderId;
        Status = CustomerBalanceReservationStatus.Active;
        Source = CustomerBalanceReservationSource.OrderCreated;
        SetAmounts(creditApplied, debtCollected);
    }

    public bool IsActive => Status == CustomerBalanceReservationStatus.Active;

    public CustomerBalanceReservationSnapshot ToSnapshot() => new(OrderId, CreditApplied, DebtCollected);

    /* Returns false when already consumed (a replayed event); a released hold cannot be consumed. */
    internal bool Consume(string reason, DateTime now) => Finalize(CustomerBalanceReservationStatus.Consumed, reason, now);

    internal bool Release(string reason, DateTime now) => Finalize(CustomerBalanceReservationStatus.Released, reason, now);

    /* An approved order modification (BR-048) only lowers the credit used; nothing grows silently. */
    internal void AdjustTo(long creditApplied, long debtCollected)
    {
        if (!IsActive || creditApplied > CreditApplied || debtCollected > DebtCollected)
        {
            throw new BusinessException(SahamErrorCodes.CustomerBalanceReservationConflict)
                .WithData("orderId", OrderId);
        }

        SetAmounts(creditApplied, debtCollected);
    }

    private bool Finalize(CustomerBalanceReservationStatus status, string reason, DateTime now)
    {
        if (Status == status)
        {
            return false;
        }

        if (!IsActive)
        {
            throw new BusinessException(SahamErrorCodes.CustomerBalanceReservationConflict)
                .WithData("orderId", OrderId);
        }

        Status = status;
        FinalizedAt = now;
        FinalizeReason = Check.Length(reason, nameof(reason), ReasonMaxLength);
        return true;
    }

    private void SetAmounts(long creditApplied, long debtCollected)
    {
        // Same rules as the database CHECKs and the calculator: non-negative, one kind only.
        if (creditApplied < 0 || debtCollected < 0 || (creditApplied > 0 && debtCollected > 0))
        {
            throw new BusinessException(SahamErrorCodes.InvalidAmount);
        }

        CreditApplied = creditApplied;
        DebtCollected = debtCollected;
    }
}
