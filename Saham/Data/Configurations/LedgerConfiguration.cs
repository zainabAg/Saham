using Microsoft.EntityFrameworkCore;
using Saham.Entities.Customers;
using Saham.Entities.Dispatch;
using Saham.Entities.Drivers;
using Saham.Entities.Ledger;
using Saham.Entities.Orders;
using Saham.Entities.Support;
using Volo.Abp.EntityFrameworkCore.Modeling;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Saham.Data.Configurations;

/* M09 Ledger & Settlements (SRS 13.4). Immutability and balance are enforced by triggers added
 * in the migration (LedgerSql): no UPDATE / DELETE on entries and lines, and every entry balanced
 * at commit time. */
public static class LedgerConfiguration
{
    public static void ConfigureSahamLedger(this ModelBuilder builder)
    {
        builder.Entity<LedgerAccount>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "LedgerAccounts", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Code).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.AccountType).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.PartyType).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            // One account per (code, party); company accounts have no party (NULLS NOT DISTINCT).
            b.HasIndex(x => new { x.Code, x.PartyType, x.PartyId }).IsUnique().AreNullsDistinct(false);
        });

        builder.Entity<JournalEntry>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "JournalEntries", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.EntryType).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.SourceType).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
            b.Property(x => x.Description).IsRequired().HasMaxLength(JournalEntry.DescriptionMaxLength);
            b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.EntryId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasOne<JournalEntry>().WithMany().HasForeignKey(x => x.ReversesEntryId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.IdempotencyKey).IsUnique();             // a financial event is posted once
            b.HasIndex(x => x.ReversesEntryId).IsUnique().HasFilter("\"ReversesEntryId\" IS NOT NULL"); // never reversed twice
            b.HasIndex(x => new { x.SourceType, x.SourceId });
            b.HasIndex(x => x.BusinessDate);
        });

        builder.Entity<JournalLine>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "JournalLines", SahamDbContext.DbSchema, t =>
                t.HasCheckConstraint("CK_JournalLines_OneSided",
                    "(\"Debit\" > 0 AND \"Credit\" = 0) OR (\"Credit\" > 0 AND \"Debit\" = 0)"));
            b.ConfigureByConvention();
            b.HasOne<LedgerAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            // Party balances (SRS 13.6): index-only sums per account
            b.HasIndex(x => x.AccountId).IncludeProperties(x => new { x.Debit, x.Credit });
            b.HasIndex(x => new { x.AccountId, x.SettlementId });
            b.HasIndex(x => x.OrderId);
        });

        builder.Entity<CashMovement>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "CashMovements", SahamDbContext.DbSchema, t =>
                t.HasCheckConstraint("CK_CashMovements_Amount", "\"Amount\" > 0"));
            b.ConfigureByConvention();
            b.Property(x => x.MovementType).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.Note).HasMaxLength(CashMovement.NoteMaxLength);
            b.HasOne<Driver>().WithMany().HasForeignKey(x => x.DriverId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<JournalEntry>().WithOne().HasForeignKey<CashMovement>(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.DriverId, x.BusinessDate });
        });

        // Credit / debt promised to open orders (BR-064, BR-071): not ledger entries, never revenue.
        builder.Entity<CustomerBalanceReservation>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "CustomerBalanceReservations", SahamDbContext.DbSchema, t =>
            {
                t.HasCheckConstraint("CK_CustomerBalanceReservations_NonNegative",
                    "\"CreditApplied\" >= 0 AND \"DebtCollected\" >= 0");
                // One signed balance gives an order credit or debt, never both.
                t.HasCheckConstraint("CK_CustomerBalanceReservations_OneKind",
                    "NOT (\"CreditApplied\" > 0 AND \"DebtCollected\" > 0)");
                t.HasCheckConstraint("CK_CustomerBalanceReservations_Finalized",
                    "(\"Status\" = 'Active') = (\"FinalizedAt\" IS NULL)");
            });
            b.ConfigureByConvention();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.Source).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.FinalizeReason).HasMaxLength(CustomerBalanceReservation.ReasonMaxLength);
            b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.OrderId).IsUnique(); // one hold per order, ever
            // What every allocation reads: the customer's active holds.
            b.HasIndex(x => x.CustomerId).HasFilter("\"Status\" = 'Active'")
                .HasDatabaseName("IX_AppCustomerBalanceReservations_ActiveByCustomer");
        });

        builder.Entity<ContactAttempt>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "ContactAttempts", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.Note).HasMaxLength(512);
            b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<DeliveryAssignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.OrderId);
            // Live in-app calls (ringing or talking) — what the call timeout and the hub look up.
            b.HasIndex(x => new { x.OrderId, x.StartedAt }).HasFilter("\"EndedAt\" IS NULL").HasDatabaseName("IX_AppContactAttempts_Live");
        });
    }
}

/* Raw SQL used by the ledger migration. */
public static class LedgerSql
{
    /* SOD-04 / BR-043: entries and lines can only be inserted. The single allowed update is
     * attaching a line to a settlement once (SettlementId NULL → value). */
    public const string CreateTriggers = """
        CREATE OR REPLACE FUNCTION saham_ledger_entries_immutable() RETURNS trigger AS $$
        BEGIN
            RAISE EXCEPTION 'Journal entries are immutable; post a reversing entry instead' USING ERRCODE = 'P0001';
        END $$ LANGUAGE plpgsql;

        CREATE TRIGGER trg_journal_entries_immutable
            BEFORE UPDATE OR DELETE ON "AppJournalEntries"
            FOR EACH ROW EXECUTE FUNCTION saham_ledger_entries_immutable();

        CREATE OR REPLACE FUNCTION saham_ledger_lines_immutable() RETURNS trigger AS $$
        BEGIN
            IF TG_OP = 'UPDATE'
               AND OLD."SettlementId" IS NULL AND NEW."SettlementId" IS NOT NULL
               AND OLD."Id" = NEW."Id" AND OLD."EntryId" = NEW."EntryId" AND OLD."AccountId" = NEW."AccountId"
               AND OLD."Debit" = NEW."Debit" AND OLD."Credit" = NEW."Credit"
               AND OLD."OrderId" IS NOT DISTINCT FROM NEW."OrderId" THEN
                RETURN NEW;
            END IF;
            RAISE EXCEPTION 'Journal lines are immutable; post a reversing entry instead' USING ERRCODE = 'P0001';
        END $$ LANGUAGE plpgsql;

        CREATE TRIGGER trg_journal_lines_immutable
            BEFORE UPDATE OR DELETE ON "AppJournalLines"
            FOR EACH ROW EXECUTE FUNCTION saham_ledger_lines_immutable();

        -- BR-044: every entry balanced, checked at commit (deferred) so all its lines are in.
        CREATE OR REPLACE FUNCTION saham_ledger_entry_balanced() RETURNS trigger AS $$
        DECLARE diff bigint; cnt int;
        BEGIN
            SELECT COALESCE(SUM("Debit") - SUM("Credit"), 0), COUNT(*) INTO diff, cnt
              FROM "AppJournalLines" WHERE "EntryId" = NEW."EntryId";
            IF diff <> 0 OR cnt < 2 THEN
                RAISE EXCEPTION 'Journal entry % is not balanced (difference %, lines %)', NEW."EntryId", diff, cnt USING ERRCODE = 'P0001';
            END IF;
            RETURN NULL;
        END $$ LANGUAGE plpgsql;

        CREATE CONSTRAINT TRIGGER trg_journal_entry_balanced
            AFTER INSERT ON "AppJournalLines"
            DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW EXECUTE FUNCTION saham_ledger_entry_balanced();
        """;

    public const string DropTriggers = """
        DROP TRIGGER IF EXISTS trg_journal_entry_balanced ON "AppJournalLines";
        DROP TRIGGER IF EXISTS trg_journal_lines_immutable ON "AppJournalLines";
        DROP TRIGGER IF EXISTS trg_journal_entries_immutable ON "AppJournalEntries";
        DROP FUNCTION IF EXISTS saham_ledger_entry_balanced();
        DROP FUNCTION IF EXISTS saham_ledger_lines_immutable();
        DROP FUNCTION IF EXISTS saham_ledger_entries_immutable();
        """;
}
