using Microsoft.EntityFrameworkCore;
using Saham.Entities.Geography;
using Saham.Entities.Restaurants;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Saham.Data.Configurations;

/* M02 Catalog: restaurants, opening hours, menu (SRS 13.4). */
public static class CatalogConfiguration
{
    public static void ConfigureSahamCatalog(this ModelBuilder builder)
    {
        builder.Entity<Restaurant>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "Restaurants", SahamDbContext.DbSchema, t =>
            {
                t.HasCheckConstraint("CK_Restaurants_CommissionRateBp", "\"CommissionRateBp\" BETWEEN 0 AND 10000");
                t.HasCheckConstraint("CK_Restaurants_DefaultPrepMinutes", "\"DefaultPrepMinutes\" BETWEEN 5 AND 120");
                t.HasCheckConstraint("CK_Restaurants_MinOrderAmount", "\"MinOrderAmount\" >= 0");
            });
            b.ConfigureByConvention();
            b.OwnsOne(x => x.Name, n => n.ToJson());
            b.Property(x => x.Phone).IsRequired().HasMaxLength(Restaurant.PhoneMaxLength);
            b.Property(x => x.Location).HasColumnType(GeographyConfiguration.PointColumnType);
            b.Property(x => x.AddressText).IsRequired().HasMaxLength(Restaurant.AddressTextMaxLength);
            b.Property(x => x.LogoUrl).HasMaxLength(Restaurant.LogoUrlMaxLength);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.PausedReason).HasMaxLength(Restaurant.PausedReasonMaxLength);
            b.HasOne<City>().WithMany().HasForeignKey(x => x.CityId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Hours).WithOne().HasForeignKey(x => x.RestaurantId).IsRequired();
            b.HasIndex(x => new { x.CityId, x.Status });
            b.HasIndex(x => x.Location).HasMethod("gist");
        });

        builder.Entity<RestaurantHours>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "RestaurantHours", SahamDbContext.DbSchema, t =>
                t.HasCheckConstraint("CK_RestaurantHours_OpensClose", "\"OpensAt\" <> \"ClosesAt\""));
            b.ConfigureByConvention();
            b.Property(x => x.DayOfWeek).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.HasIndex(x => new { x.RestaurantId, x.DayOfWeek });
        });

        builder.Entity<MenuCategory>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "MenuCategories", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.OwnsOne(x => x.Name, n => n.ToJson());
            b.HasOne<Restaurant>().WithMany().HasForeignKey(x => x.RestaurantId).OnDelete(DeleteBehavior.Restrict);
            // Target of the composite FK from MenuItems: (Id, RestaurantId).
            b.HasAlternateKey(x => new { x.Id, x.RestaurantId });
            b.HasIndex(x => new { x.RestaurantId, x.SortOrder });
        });

        builder.Entity<MenuItem>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "MenuItems", SahamDbContext.DbSchema, t =>
                t.HasCheckConstraint("CK_MenuItems_Price", "\"Price\" > 0"));
            b.ConfigureByConvention();
            b.OwnsOne(x => x.Name, n => n.ToJson());
            b.OwnsOne(x => x.Description, n => n.ToJson());
            b.Property(x => x.ImageUrl).HasMaxLength(MenuItem.ImageUrlMaxLength);
            b.HasOne<Restaurant>().WithMany().HasForeignKey(x => x.RestaurantId).OnDelete(DeleteBehavior.Restrict);
            // The category must belong to the same restaurant as the item.
            b.HasOne<MenuCategory>().WithMany()
                .HasForeignKey(x => new { x.CategoryId, x.RestaurantId })
                .HasPrincipalKey(c => new { c.Id, c.RestaurantId })
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.RestaurantId, x.CategoryId, x.SortOrder });
        });

        builder.Entity<RestaurantStaff>(b =>
        {
            b.HasOne<Restaurant>().WithMany().HasForeignKey(x => x.RestaurantId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
