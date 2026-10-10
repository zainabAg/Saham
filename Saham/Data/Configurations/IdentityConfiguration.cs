using Microsoft.EntityFrameworkCore;
using Saham.Entities.Customers;
using Saham.Entities.Identity;
using Saham.Entities.Restaurants;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Saham.Data.Configurations;

/* M01 Identity & Access: OTP challenges, customer profiles, restaurant staff links. */
public static class IdentityConfiguration
{
    public static void ConfigureSahamIdentity(this ModelBuilder builder)
    {
        builder.Entity<OtpChallenge>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "OtpChallenges", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.PhoneNumber).IsRequired().HasMaxLength(SahamConsts.PhoneNumberMaxLength);
            b.Property(x => x.CodeHash).IsRequired().HasMaxLength(128);
            b.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.HasIndex(x => new { x.PhoneNumber, x.Purpose, x.CreationTime });
            b.HasIndex(x => x.ExpiresAt);
        });

        builder.Entity<CustomerProfile>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "CustomerProfiles", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Id).ValueGeneratedNever();
            b.ToTable(t => t.HasCheckConstraint("CK_CustomerProfiles_NoShowCount", "\"NoShowCount\" >= 0"));
        });

        builder.Entity<RestaurantStaff>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "RestaurantStaff", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Role).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            // A user works for at most one restaurant.
            b.HasIndex(x => x.UserId).IsUnique().HasFilter("\"IsDeleted\" = false");
            b.HasIndex(x => new { x.RestaurantId, x.Role });
        });
    }
}
