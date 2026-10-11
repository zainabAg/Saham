using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Saham.Entities.Geography;
using Saham.Entities.Pricing;
using Saham.Entities.Restaurants;
using Volo.Abp.EntityFrameworkCore.Modeling;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Saham.Data.Configurations;

/* M04 Pricing & Promotions: pricing plans, distance bands, quotes (SRS 13.4). */
public static class PricingConfiguration
{
    public static void IgnoreJsonValueTypes(ModelBuilder builder)
    {
        builder.Ignore<QuoteSnapshot>();
        builder.Ignore<QuoteLineSnapshot>();
        builder.Ignore<QuoteAddressSnapshot>();
        builder.Ignore<QuoteText>();
        builder.Ignore<PriceBreakdown>();
        builder.Ignore<QuoteCouponSnapshot>();
    }

    public static void ConfigureSahamPricing(this ModelBuilder builder)
    {
        builder.Entity<PricingPlan>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "PricingPlans", SahamDbContext.DbSchema, t =>
            {
                t.HasCheckConstraint("CK_PricingPlans_Amounts",
                    "\"DriverBasePay\" >= 0 AND \"BatchSecondOrderPay\" >= 0 AND \"FreeDeliveryThreshold\" >= 0 " +
                    "AND \"MaxDeliveryDistanceM\" > 0 AND \"CancelAfterArrivalPayPct\" BETWEEN 0 AND 100");
            });
            b.ConfigureByConvention();
            b.Property(x => x.Name).IsRequired().HasMaxLength(PricingPlan.NameMaxLength);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.FreeDeliveryOperator).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.FreeDeliveryBasis).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.DistanceMethod).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.HasOne<City>().WithMany().HasForeignKey(x => x.CityId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Tiers).WithOne().HasForeignKey(x => x.PricingPlanId).IsRequired();
            // One active plan per city at any time (SRS 13.4 pricing_plans).
            b.HasIndex(x => x.CityId).IsUnique().HasFilter("\"Status\" = 'Active'")
                .HasDatabaseName("IX_AppPricingPlans_CityId_Active");
        });

        builder.Entity<PricingTier>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "PricingTiers", SahamDbContext.DbSchema, t =>
            {
                t.HasCheckConstraint("CK_PricingTiers_Range", "\"ToM\" > \"FromM\" AND \"FromM\" >= 0");
                t.HasCheckConstraint("CK_PricingTiers_Amounts", "\"CustomerFee\" >= 0 AND \"DriverExtra\" >= 0");
            });
            b.ConfigureByConvention();
            b.HasIndex(x => new { x.PricingPlanId, x.FromM }).IsUnique();
        });

        builder.Entity<Quote>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "Quotes", SahamDbContext.DbSchema, t =>
                t.HasCheckConstraint("CK_Quotes_AmountDue", "\"AmountDue\" >= 0"));
            b.ConfigureByConvention();
            b.Property(x => x.OrderType).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.PayloadHash).IsRequired().HasMaxLength(128);
            b.Property(x => x.Snapshot)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<QuoteSnapshot>(v, (JsonSerializerOptions?)null)!,
                    // The snapshot is immutable after creation: compare by reference.
                    new ValueComparer<QuoteSnapshot>((a, c) => ReferenceEquals(a, c), v => v.GetHashCode(), v => v));
            b.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Restaurant>().WithMany().HasForeignKey(x => x.RestaurantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CustomerAddress>().WithMany().HasForeignKey(x => x.AddressId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PricingPlan>().WithMany().HasForeignKey(x => x.PricingPlanId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.ConsumedOrderId).IsUnique().HasFilter("\"ConsumedOrderId\" IS NOT NULL");
            b.HasIndex(x => new { x.CustomerId, x.CreationTime });
            b.HasIndex(x => x.ExpiresAt);
        });
    }
}
