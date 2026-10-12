namespace Saham.Data.Configurations;

/* Phase 10 transition for orders that were open before reservations existed. Their DebtCollected /
 * CreditApplied were frozen from the raw ledger balance and will still be posted at delivery, so they
 * ARE promises on the balance: each gets an Active hold (Source = LegacyBackfill), never "nothing held".
 * Finished orders need none: what they posted is already in the ledger.
 * A customer whose old open orders promised more than the ledger holds (the double allocation this
 * phase prevents) is not "repaired" here: new allocations fail closed for them until those orders end,
 * and ReconciliationQuery lists them for the accountant (see docs/balance-reservation-phase2.md). */
public static class CustomerBalanceReservationSql
{
    public const string BackfillOpenOrders = """
        INSERT INTO "AppCustomerBalanceReservations"
            ("Id", "CustomerId", "OrderId", "CreditApplied", "DebtCollected", "Status", "Source",
             "FinalizedAt", "FinalizeReason", "ExtraProperties", "ConcurrencyStamp", "CreationTime")
        SELECT gen_random_uuid(), o."CustomerId", o."Id", o."CreditApplied", o."DebtCollected", 'Active', 'LegacyBackfill',
               NULL, NULL, '{}', replace(gen_random_uuid()::text, '-', ''), (now() AT TIME ZONE 'utc')
          FROM "AppOrders" o
         WHERE o."Status" NOT IN ('Delivered', 'Cancelled', 'DeliveryFailed')
           AND (o."CreditApplied" > 0 OR o."DebtCollected" > 0)
        ON CONFLICT ("OrderId") DO NOTHING;
        """;

    /* Read-only: customers whose active holds exceed the matching posted amount (credit > positive balance,
     * or debt > debt). CUSTOMER_BALANCE is a liability: posted balance = Σ credit − Σ debit. */
    public const string ReconciliationQuery = """
        WITH posted AS (
            SELECT a."PartyId" AS "CustomerId", COALESCE(SUM(l."Credit" - l."Debit"), 0) AS "Balance"
              FROM "AppLedgerAccounts" a
              LEFT JOIN "AppJournalLines" l ON l."AccountId" = a."Id"
             WHERE a."Code" = 'CustomerBalance' AND a."PartyId" IS NOT NULL
             GROUP BY a."PartyId"),
        held AS (
            SELECT "CustomerId", SUM("CreditApplied") AS "Credit", SUM("DebtCollected") AS "Debt", COUNT(*) AS "Holds"
              FROM "AppCustomerBalanceReservations"
             WHERE "Status" = 'Active'
             GROUP BY "CustomerId")
        SELECT h."CustomerId", COALESCE(p."Balance", 0) AS "PostedBalance", h."Credit", h."Debt", h."Holds"
          FROM held h
          LEFT JOIN posted p ON p."CustomerId" = h."CustomerId"
         WHERE h."Credit" > GREATEST(COALESCE(p."Balance", 0), 0)
            OR h."Debt" > GREATEST(-COALESCE(p."Balance", 0), 0);
        """;
}
