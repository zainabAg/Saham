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
using Saham.Entities.Customers;
using Saham.Entities.Geography;
using Saham.Entities.Identity;
using Saham.Entities.Notifications;
using Saham.Entities.Orders;
using Saham.Entities.Dispatch;
using Saham.Entities.Drivers;
using Saham.Entities.Support;
using Saham.Entities.Ledger;
using Saham.Entities.Pricing;
using Saham.Entities.Restaurants;
using Saham.Infrastructure.Idempotency;

namespace Saham.Data;

public class SahamDbContext : AbpDbContext<SahamDbContext>
{
    // M01 Identity & Access
    public DbSet<OtpChallenge> OtpChallenges { get; set; }
    public DbSet<CustomerProfile> CustomerProfiles { get; set; }
    public DbSet<RestaurantStaff> RestaurantStaff { get; set; }

    // M02 Catalog
    public DbSet<Restaurant> Restaurants { get; set; }
    public DbSet<RestaurantHours> RestaurantHours { get; set; }
    public DbSet<MenuCategory> MenuCategories { get; set; }
    public DbSet<MenuItem> MenuItems { get; set; }

    // M03 Geo
    public DbSet<City> Cities { get; set; }
    public DbSet<CustomerAddress> CustomerAddresses { get; set; }

    // M04 Pricing & Promotions
    public DbSet<PricingPlan> PricingPlans { get; set; }
    public DbSet<PricingTier> PricingTiers { get; set; }
    public DbSet<Quote> Quotes { get; set; }

    // M05 Orders
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<OrderStatusHistory> OrderStatusHistory { get; set; }
    public DbSet<OrderModification> OrderModifications { get; set; }

    // M06 Dispatch & M07 Driver Ops
    public DbSet<Driver> Drivers { get; set; }
    public DbSet<DriverDocument> DriverDocuments { get; set; }
    public DbSet<DriverLocation> DriverLocations { get; set; }
    public DbSet<DeliveryOffer> DeliveryOffers { get; set; }
    public DbSet<DeliveryAssignment> DeliveryAssignments { get; set; }

    // M11 Support & Incidents
    public DbSet<Incident> Incidents { get; set; }

    // M09 Ledger & Settlements
    public DbSet<LedgerAccount> LedgerAccounts { get; set; }
    public DbSet<JournalEntry> JournalEntries { get; set; }
    public DbSet<JournalLine> JournalLines { get; set; }
    public DbSet<CashMovement> CashMovements { get; set; }
    public DbSet<ContactAttempt> ContactAttempts { get; set; }

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
        // Value records stored as JSON columns; must not be discovered as entities
        // (ABP's base configuration would otherwise pin them as entity types).
        PricingConfiguration.IgnoreJsonValueTypes(builder);

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
        
        /* Configure your own entities here: one extension method per module in Data/Configurations */
        builder.ConfigureSahamIdentity();
        builder.ConfigureSahamGeography();
        builder.ConfigureSahamCatalog();
        builder.ConfigureSahamPricing();
        builder.ConfigureSahamOrders();
        builder.ConfigureSahamDriversAndDispatch();
        builder.ConfigureSahamLedger();
        builder.ConfigureSahamNotifications();
        builder.ConfigureSahamInfrastructure();
    }
}

