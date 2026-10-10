using Microsoft.EntityFrameworkCore;
using Saham.Entities.Geography;
using Saham.Entities.Orders;
using Saham.Entities.Pricing;
using Saham.Entities.Restaurants;
using Volo.Abp.EntityFrameworkCore.Modeling;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Saham.Data.Configurations;

/* M05 Orders (SRS 13.4). The amount equations are CHECK constraints, so an order with an
 * inconsistent total cannot be stored even if the code is wrong (SRS 13.8 §1). */
public static class OrdersConfiguration
{
    public const string OrderNumberSequence = "AppOrderNumbers";

    public static void ConfigureSahamOrders(this ModelBuilder builder)
    {
        builder.HasSequence<long>(OrderNumberSequence).StartsAt(1001);

        builder.Entity<Order>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "Orders", SahamDbContext.DbSchema, t =>
            {
                t.HasCheckConstraint("CK_Orders_Total",
                    "\"TotalAmount\" = \"ItemsSubtotal\" - \"DiscountAmount\" + \"DeliveryFeeCharged\"");
                t.HasCheckConstraint("CK_Orders_Discount",
                    "\"DiscountAmount\" = \"CompanyDiscount\" + \"RestaurantDiscount\" AND \"DiscountAmount\" <= \"ItemsSubtotal\"");
                t.HasCheckConstraint("CK_Orders_RestaurantNet",
                    "\"RestaurantNetAmount\" = \"ItemsSubtotal\" - \"RestaurantDiscount\" - \"CommissionAmount\"");
                t.HasCheckConstraint("CK_Orders_AmountDue",
                    "\"AmountDue\" = \"TotalAmount\" + \"DebtCollected\" - \"CreditApplied\"");
                t.HasCheckConstraint("CK_Orders_NonNegative",
                    "\"ItemsSubtotal\" >= 0 AND \"CompanyDiscount\" >= 0 AND \"RestaurantDiscount\" >= 0 " +
                    "AND \"DeliveryFeeBase\" >= 0 AND \"DeliveryFeeCharged\" >= 0 AND \"CommissionAmount\" >= 0 " +
                    "AND \"RestaurantNetAmount\" >= 0 AND \"DriverPayAmount\" >= 0 AND \"DebtCollected\" >= 0 " +
                    "AND \"CreditApplied\" >= 0 AND \"AmountDue\" >= 0");
                t.HasCheckConstraint("CK_Orders_CashTendered",
                    "\"CashTendered\" IS NULL OR \"CashTendered\" >= \"AmountDue\"");
                // INV-06: a held order has its dispatch paused or not started.
                t.HasCheckConstraint("CK_Orders_HoldPausesDispatch",
                    "\"HoldReason\" IS NULL OR \"DispatchStatus\" IN ('Paused', 'NotStarted', 'Assigned')");
            });
            b.ConfigureByConvention();

            b.Property(x => x.Number).HasDefaultValueSql($"nextval('\"{OrderNumberSequence}\"')").ValueGeneratedOnAdd();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.HoldReason).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.DispatchStatus).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.OrderType).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.CancelReason).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.CancelledByType).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.FailureReason).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.CancelNote).HasMaxLength(512);
            b.Property(x => x.DeliveryLocation).HasColumnType(GeographyConfiguration.PointColumnType);
            b.Property(x => x.DeliveryDescription).IsRequired().HasMaxLength(CustomerAddress.DescriptionMaxLength);
            b.Property(x => x.AddressLabel).IsRequired().HasMaxLength(CustomerAddress.CustomLabelMaxLength);
            b.Property(x => x.RecipientName).HasMaxLength(SahamConsts.NameMaxLength);
            b.Property(x => x.RecipientPhone).HasMaxLength(SahamConsts.PhoneNumberMaxLength);

            b.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Restaurant>().WithMany().HasForeignKey(x => x.RestaurantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<City>().WithMany().HasForeignKey(x => x.CityId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Quote>().WithMany().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PricingPlan>().WithMany().HasForeignKey(x => x.PricingPlanId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.OrderId).IsRequired();
            b.HasMany(x => x.History).WithOne().HasForeignKey(x => x.OrderId).IsRequired();
            b.HasMany(x => x.Modifications).WithOne().HasForeignKey(x => x.OrderId).IsRequired();

            b.HasIndex(x => x.Number).IsUnique();
            b.HasIndex(x => x.QuoteId).IsUnique(); // what the customer saw is what is stored (SRS 13.5)
            // SRS 13.6 frequent queries
            b.HasIndex(x => new { x.Status, x.CreationTime })
                .HasFilter("\"Status\" NOT IN ('Delivered', 'Cancelled', 'DeliveryFailed')")
                .HasDatabaseName("IX_AppOrders_Live");
            b.HasIndex(x => new { x.RestaurantId, x.Status, x.CreationTime });
            b.HasIndex(x => new { x.CustomerId, x.CreationTime });
            b.HasIndex(x => x.ReleaseAt).HasFilter("\"Status\" = 'Scheduled'").HasDatabaseName("IX_AppOrders_ScheduledRelease");
            b.HasIndex(x => x.ItemDecisionDeadlineAt).HasFilter("\"HoldReason\" IS NOT NULL").HasDatabaseName("IX_AppOrders_ItemDecision");
        });

        builder.Entity<OrderItem>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "OrderItems", SahamDbContext.DbSchema, t =>
                t.HasCheckConstraint("CK_OrderItems_Amounts",
                    "\"Quantity\" > 0 AND \"UnitPrice\" > 0 AND \"LineTotal\" = \"UnitPrice\" * \"Quantity\""));
            b.ConfigureByConvention();
            b.OwnsOne(x => x.NameSnapshot, n => n.ToJson());
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.RemovedReason).HasMaxLength(64);
            b.HasOne<MenuItem>().WithMany().HasForeignKey(x => x.MenuItemId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<OrderStatusHistory>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "OrderStatusHistory", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.EventType).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.ActorType).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.Reason).HasMaxLength(512);
            b.HasIndex(x => new { x.OrderId, x.OccurredAt });
        });

        builder.Entity<OrderModification>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "OrderModifications", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ModificationType).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.RequestedByType).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.Before).IsRequired().HasColumnType("jsonb");
            b.Property(x => x.After).IsRequired().HasColumnType("jsonb");
            b.HasIndex(x => x.OrderId);
        });
    }
}
