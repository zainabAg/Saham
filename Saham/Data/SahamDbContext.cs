using Microsoft.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.BlobStoring.Database.EntityFrameworkCore;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Saham.Data.Configurations;
using Saham.Entities.Books;
using Saham.Entities.Customers;
using Saham.Entities.Identity;
using Saham.Entities.Notifications;
using Saham.Entities.Restaurants;
using Saham.Infrastructure.Idempotency;

namespace Saham.Data;

public class SahamDbContext : AbpDbContext<SahamDbContext>
{
    public DbSet<Book> Books { get; set; }

    // M01 Identity & Access
    public DbSet<OtpChallenge> OtpChallenges { get; set; }
    public DbSet<CustomerProfile> CustomerProfiles { get; set; }
    public DbSet<RestaurantStaff> RestaurantStaff { get; set; }

    // M10 Notifications
    public DbSet<SmsLog> SmsLogs { get; set; }

    // Infrastructure
    public DbSet<IdempotencyRecord> IdempotencyRecords { get; set; }
    
    public const string DbTablePrefix = "App";
    public const string DbSchema = null;

    public SahamDbContext(DbContextOptions<SahamDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("postgis");

        /* Include modules to your migration db context */

        builder.ConfigureSettingManagement();
        builder.ConfigureBackgroundJobs();
        builder.ConfigureAuditLogging();
        builder.ConfigureFeatureManagement();
        builder.ConfigurePermissionManagement();
        builder.ConfigureBlobStoring();
        builder.ConfigureIdentity();
        builder.ConfigureOpenIddict();
        
        builder.Entity<Book>(b =>
        {
            b.ToTable(DbTablePrefix + "Books",
                DbSchema);
            b.ConfigureByConvention(); //auto configure for the base class props
            b.Property(x => x.Name).IsRequired().HasMaxLength(128);
        });
        
        /* Configure your own entities here: one extension method per module in Data/Configurations */
        builder.ConfigureSahamIdentity();
        builder.ConfigureSahamNotifications();
        builder.ConfigureSahamInfrastructure();
    }
}

