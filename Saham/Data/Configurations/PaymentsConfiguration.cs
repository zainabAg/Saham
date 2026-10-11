using Microsoft.EntityFrameworkCore;
using Saham.Entities.Coupons;
using Saham.Entities.Payments;
using Saham.Entities.Settlements;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Saham.Data.Configurations;

/* M08 Payments & refunds (SRS 13.4): one live payment per order, refunds ≤ the captured amount
 * (BR-042, enforced in PaymentManager with the payment's concurrency stamp), one stored row per
 * provider event (E3). */
public static class PaymentsConfiguration
{
    public static void ConfigureSahamPayments(this ModelBuilder builder)
    {
        builder.Entity<Payment>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "Payments", SahamDbContext.DbSchema, t =>
            {
                t.HasCheckConstraint("CK_Payments_Amounts",
                    "\"Amount\" > 0 AND \"CapturedAmount\" >= 0 AND \"RefundedAmount\" >= 0 AND \"RefundedAmount\" <= \"CapturedAmount\"");
            });
            b.ConfigureByConvention();
            b.Property(x => x.Method).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.Provider).IsRequired().HasMaxLength(Payment.ProviderMaxLength);
            b.Property(x => x.ProviderTxnId).HasMaxLength(128);
            b.Property(x => x.RedirectUrl).HasMaxLength(512);
            b.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
            b.Property(x => x.FailureReason).HasMaxLength(256);
            b.HasIndex(x => x.IdempotencyKey).IsUnique();
            b.HasIndex(x => new { x.Provider, x.ProviderTxnId }).IsUnique().HasFilter("\"ProviderTxnId\" IS NOT NULL");
            // One live payment per order; a failed one may be followed by another (SRS 13.5).
            b.HasIndex(x => x.OrderId).IsUnique()
                .HasFilter("\"Status\" IN ('Initiated', 'Pending', 'Captured') AND \"IsDeleted\" = false")
                .HasDatabaseName("IX_AppPayments_OneLivePerOrder");
            b.HasIndex(x => new { x.Status, x.ExpiresAt });
        });

        builder.Entity<PaymentEvent>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "PaymentEvents", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Provider).IsRequired().HasMaxLength(Payment.ProviderMaxLength);
            b.Property(x => x.ProviderEventId).IsRequired().HasMaxLength(128);
            b.Property(x => x.EventType).IsRequired().HasMaxLength(64);
            b.Property(x => x.Payload).IsRequired().HasColumnType("jsonb");
            b.Property(x => x.ProcessingError).HasMaxLength(256);
            b.HasIndex(x => new { x.Provider, x.ProviderEventId }).IsUnique();
            b.HasIndex(x => x.PaymentId);
        });

        builder.Entity<Refund>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "Refunds", SahamDbContext.DbSchema, t =>
                t.HasCheckConstraint("CK_Refunds_Amount", "\"Amount\" > 0"));
            b.ConfigureByConvention();
            b.Property(x => x.Reason).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.ResolutionChannel).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
            b.Property(x => x.ProviderRefundId).HasMaxLength(128);
            b.Property(x => x.LastError).HasMaxLength(Refund.ErrorMaxLength);
            b.Property(x => x.ResolutionReference).HasMaxLength(Refund.ReferenceMaxLength);
            b.HasIndex(x => x.IdempotencyKey).IsUnique();
            b.HasIndex(x => x.ProviderRefundId).IsUnique().HasFilter("\"ProviderRefundId\" IS NOT NULL");
            b.HasIndex(x => x.OrderId);
            b.HasIndex(x => new { x.Status, x.NextAttemptAt });
        });
    }

    /* M09 Coupons (BR-010..012, BR-072). */
    public static void ConfigureSahamCoupons(this ModelBuilder builder)
    {
        builder.Entity<Coupon>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "Coupons", SahamDbContext.DbSchema, t =>
            {
                t.HasCheckConstraint("CK_Coupons_Percent", "\"DiscountType\" <> 'Percent' OR \"Value\" BETWEEN 1 AND 100");
                t.HasCheckConstraint("CK_Coupons_RestaurantFunded", "\"FundedBy\" <> 'Restaurant' OR \"RestaurantId\" IS NOT NULL");
                t.HasCheckConstraint("CK_Coupons_UsedCount", "\"UsedCount\" >= 0 AND (\"UsageLimitTotal\" IS NULL OR \"UsedCount\" <= \"UsageLimitTotal\")");
                t.HasCheckConstraint("CK_Coupons_ActiveApproved", "\"Status\" <> 'Active' OR \"ApprovedBy\" IS NOT NULL");
            });
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(Coupon.CodeMaxLength);
            b.Property(x => x.DiscountType).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.FundedBy).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.RejectionReason).HasMaxLength(Coupon.ReasonMaxLength);
            // Codes are stored upper-case, so the plain unique index is the "lower(code)" uniqueness of the SRS.
            b.HasIndex(x => x.Code).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.HasIndex(x => new { x.Status, x.RestaurantId });
        });

        builder.Entity<CouponRedemption>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "CouponRedemptions", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.HasIndex(x => x.OrderId).IsUnique();
            b.HasIndex(x => new { x.CouponId, x.CustomerId, x.Status });
        });
    }

    /* M12 Settlements (BR-040, BR-068). */
    public static void ConfigureSahamSettlements(this ModelBuilder builder)
    {
        builder.Entity<Settlement>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "Settlements", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.SettlementType).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.PartyType).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.PaidChannel).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.PaidReference).HasMaxLength(Settlement.ReferenceMaxLength);
            b.HasIndex(x => new { x.SettlementType, x.PartyType, x.PartyId, x.PeriodStart }).IsUnique();
            b.HasIndex(x => new { x.Status, x.SettlementType });
        });
    }
}

/* BR-040 / UC-24: once a statement is confirmed its figures never change (only its status moves on
 * to PAID / COLLECTED / CLOSED with the payment details). */
public static class SettlementSql
{
    public const string CreateTriggers = """
        CREATE OR REPLACE FUNCTION saham_settlement_frozen() RETURNS trigger AS $$
        BEGIN
            IF OLD."Status" <> 'Draft' AND (
                   NEW."OpeningBalance" <> OLD."OpeningBalance" OR NEW."TotalDebits" <> OLD."TotalDebits"
                OR NEW."TotalCredits" <> OLD."TotalCredits" OR NEW."NetAmount" <> OLD."NetAmount"
                OR NEW."OrdersCount" <> OLD."OrdersCount" OR NEW."PartyId" <> OLD."PartyId"
                OR NEW."PeriodStart" <> OLD."PeriodStart" OR NEW."PeriodEnd" <> OLD."PeriodEnd") THEN
                RAISE EXCEPTION 'Confirmed statement % cannot change', OLD."Id" USING ERRCODE = 'P0001';
            END IF;
            RETURN NEW;
        END $$ LANGUAGE plpgsql;

        CREATE TRIGGER trg_settlements_frozen
            BEFORE UPDATE ON "AppSettlements"
            FOR EACH ROW EXECUTE FUNCTION saham_settlement_frozen();
        """;

    public const string DropTriggers = """
        DROP TRIGGER IF EXISTS trg_settlements_frozen ON "AppSettlements";
        DROP FUNCTION IF EXISTS saham_settlement_frozen();
        """;
}
