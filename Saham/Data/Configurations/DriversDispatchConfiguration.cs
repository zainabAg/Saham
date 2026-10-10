using Microsoft.EntityFrameworkCore;
using Saham.Entities.Dispatch;
using Saham.Entities.Drivers;
using Saham.Entities.Geography;
using Saham.Entities.Orders;
using Saham.Entities.Restaurants;
using Saham.Entities.Support;
using Volo.Abp.EntityFrameworkCore.Modeling;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Saham.Data.Configurations;

/* M07 Driver Ops, M06 Dispatch, M11 Incidents (SRS 13.4).
 * The guarantees against double assignment live here, not only in code (EX-06):
 *   - one pending offer per order, one pending offer per driver, never the same order twice to a driver;
 *   - one active assignment per order;
 *   - a driver never above two active orders. */
public static class DriversDispatchConfiguration
{
    public static void ConfigureSahamDriversAndDispatch(this ModelBuilder builder)
    {
        builder.Entity<Driver>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "Drivers", SahamDbContext.DbSchema, t =>
            {
                t.HasCheckConstraint("CK_Drivers_ActiveOrderCount", "\"ActiveOrderCount\" BETWEEN 0 AND 2"); // BR-030
                t.HasCheckConstraint("CK_Drivers_AvailableOnlyIfApproved",
                    "\"Availability\" = 'Offline' OR \"ReviewStatus\" = 'Approved'");
                t.HasCheckConstraint("CK_Drivers_ApprovedBy", "\"ReviewStatus\" <> 'Approved' OR \"ApprovedBy\" IS NOT NULL"); // SOD-07
                t.HasCheckConstraint("CK_Drivers_FloatAmount", "\"FloatAmount\" >= 0");
            });
            b.ConfigureByConvention();
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ReviewStatus).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.Availability).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.CashBlocked).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.ReviewNote).HasMaxLength(Driver.ReviewNoteMaxLength);
            b.Property(x => x.VehiclePlate).HasMaxLength(Driver.VehiclePlateMaxLength);
            b.Property(x => x.EmergencyContactPhone).HasMaxLength(SahamConsts.PhoneNumberMaxLength);
            b.Property(x => x.LastLocation).HasColumnType(GeographyConfiguration.PointColumnType);
            b.HasOne<IdentityUser>().WithOne().HasForeignKey<Driver>(x => x.Id).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<City>().WithMany().HasForeignKey(x => x.CityId).OnDelete(DeleteBehavior.Restrict);
            // "Nearest available driver" (SRS 13.6)
            b.HasIndex(x => x.LastLocation).HasMethod("gist").HasFilter("\"Availability\" = 'Available'")
                .HasDatabaseName("IX_AppDrivers_AvailableLocation");
            b.HasIndex(x => new { x.ReviewStatus, x.Availability });
        });

        builder.Entity<DriverDocument>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "DriverDocuments", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.DocType).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.BlobName).IsRequired().HasMaxLength(256);
            b.Property(x => x.ContentType).IsRequired().HasMaxLength(64);
            b.Property(x => x.ReviewNote).HasMaxLength(512);
            b.HasOne<Driver>().WithMany().HasForeignKey(x => x.DriverId).OnDelete(DeleteBehavior.Restrict);
            // One live document per type (SRS 13.4)
            b.HasIndex(x => new { x.DriverId, x.DocType }).IsUnique().HasFilter("\"Status\" <> 'Rejected'")
                .HasDatabaseName("IX_AppDriverDocuments_OneLivePerType");
        });

        builder.Entity<DriverLocation>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "DriverLocations", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Location).HasColumnType(GeographyConfiguration.PointColumnType);
            b.HasIndex(x => new { x.DriverId, x.RecordedAt });
            b.HasIndex(x => x.ReceivedAt).HasMethod("brin");
        });

        builder.Entity<DeliveryOffer>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "DeliveryOffers", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Driver>().WithMany().HasForeignKey(x => x.DriverId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.OrderId).IsUnique().HasFilter("\"Status\" = 'Pending'")
                .HasDatabaseName("IX_AppDeliveryOffers_OnePendingPerOrder");
            b.HasIndex(x => x.DriverId).IsUnique().HasFilter("\"Status\" = 'Pending'")
                .HasDatabaseName("IX_AppDeliveryOffers_OnePendingPerDriver");
            b.HasIndex(x => new { x.OrderId, x.DriverId }).IsUnique(); // never the same order twice to a driver
            b.HasIndex(x => x.ExpiresAt).HasFilter("\"Status\" = 'Pending'").HasDatabaseName("IX_AppDeliveryOffers_PendingExpiry");
        });

        builder.Entity<DeliveryAssignment>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "DeliveryAssignments", SahamDbContext.DbSchema, t =>
                t.HasCheckConstraint("CK_DeliveryAssignments_Pay", "\"DriverPayAmount\" >= 0"));
            b.ConfigureByConvention();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.AssignedByType).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.ReleaseReason).HasMaxLength(512);
            b.Property(x => x.DeliveryLocation).HasColumnType(GeographyConfiguration.PointColumnType);
            b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Driver>().WithMany().HasForeignKey(x => x.DriverId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<DeliveryOffer>().WithMany().HasForeignKey(x => x.OfferId).OnDelete(DeleteBehavior.Restrict);
            // The final guarantee: one active assignment per order (EX-06, TC-14)
            b.HasIndex(x => x.OrderId).IsUnique().HasFilter("\"Status\" IN ('Assigned', 'PickedUp')")
                .HasDatabaseName("IX_AppDeliveryAssignments_OneActivePerOrder");
            b.HasIndex(x => x.DriverId).HasFilter("\"Status\" IN ('Assigned', 'PickedUp')")
                .HasDatabaseName("IX_AppDeliveryAssignments_ActiveByDriver");
        });

        builder.Entity<Incident>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "Incidents", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.IncidentType).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.Severity).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.DedupeKey).IsRequired().HasMaxLength(128);
            b.Property(x => x.Resolution).HasMaxLength(512);
            b.HasIndex(x => x.DedupeKey).IsUnique().HasFilter("\"Status\" <> 'Resolved'")
                .HasDatabaseName("IX_AppIncidents_OneOpenPerKey");
            b.HasIndex(x => new { x.Status, x.OpenedAt });
        });
    }
}
