using Microsoft.EntityFrameworkCore;
using Saham.Entities.Geography;
using Volo.Abp.EntityFrameworkCore.Modeling;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Saham.Data.Configurations;

/* M03 Geo: cities and customer addresses (PostGIS geography, SRID 4326). */
public static class GeographyConfiguration
{
    public const string PointColumnType = "geography (point, 4326)";
    public const string AreaColumnType = "geography (multipolygon, 4326)";

    public static void ConfigureSahamGeography(this ModelBuilder builder)
    {
        builder.Entity<City>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "Cities", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.OwnsOne(x => x.Name, n => n.ToJson());
            b.Property(x => x.TimeZone).IsRequired().HasMaxLength(64);
            b.Property(x => x.Center).HasColumnType(PointColumnType);
            b.Property(x => x.ServiceArea).HasColumnType(AreaColumnType);
        });

        builder.Entity<CustomerAddress>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "CustomerAddresses", SahamDbContext.DbSchema, t =>
                t.HasCheckConstraint("CK_CustomerAddresses_Description", "char_length(\"Description\") >= 1"));
            b.ConfigureByConvention();
            b.Property(x => x.Label).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.CustomLabel).HasMaxLength(CustomerAddress.CustomLabelMaxLength);
            b.Property(x => x.Location).HasColumnType(PointColumnType);
            b.Property(x => x.Description).IsRequired().HasMaxLength(CustomerAddress.DescriptionMaxLength);
            b.Property(x => x.RecipientName).HasMaxLength(SahamConsts.NameMaxLength);
            b.Property(x => x.RecipientPhone).HasMaxLength(SahamConsts.PhoneNumberMaxLength);
            b.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<City>().WithMany().HasForeignKey(x => x.CityId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.UserId);
        });
    }
}
