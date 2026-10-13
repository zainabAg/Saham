using Microsoft.EntityFrameworkCore;
using Saham.Entities.Notifications;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Saham.Data.Configurations;

/* M10 Notifications: the SMS log, the outbox, the notification queue (SRS 13.4, I-6) and push devices. */
public static class NotificationsConfiguration
{
    public static void ConfigureSahamNotifications(this ModelBuilder builder)
    {
        builder.Entity<SmsLog>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "SmsLogs", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.PhoneNumber).IsRequired().HasMaxLength(SahamConsts.PhoneNumberMaxLength);
            b.Property(x => x.Text).IsRequired().HasMaxLength(1024);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.Provider).HasMaxLength(64);
            b.Property(x => x.ProviderMessageId).HasMaxLength(128);
            b.Property(x => x.LastError).HasMaxLength(1024);
            b.Property(x => x.IdempotencyKey).HasMaxLength(64);
            b.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("\"IdempotencyKey\" IS NOT NULL");
            b.HasIndex(x => new { x.PhoneNumber, x.CreationTime });
            b.HasIndex(x => x.CreationTime);
        });

        builder.Entity<OutboxEvent>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "OutboxEvents", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Id).UseIdentityByDefaultColumn(); // bigserial
            b.Property(x => x.EventType).IsRequired().HasMaxLength(64);
            b.Property(x => x.EventKey).IsRequired().HasMaxLength(160);
            b.Property(x => x.AggregateType).IsRequired().HasMaxLength(64);
            b.Property(x => x.Payload).IsRequired().HasColumnType("jsonb");
            b.Property(x => x.LastError).HasMaxLength(1024);
            // Phase 6: one row per logical change; a retry with the same key and content reuses it.
            b.HasIndex(x => x.EventKey).IsUnique().HasDatabaseName("UX_AppOutboxEvents_EventKey");
            // The relay only ever reads the unpublished tail.
            b.HasIndex(x => x.Id).HasFilter("\"PublishedAt\" IS NULL").HasDatabaseName("IX_AppOutboxEvents_Unpublished");
            b.HasIndex(x => new { x.AggregateType, x.AggregateId });
        });

        builder.Entity<Notification>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "Notifications", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.EventKey).IsRequired().HasMaxLength(160);
            b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.Phone).HasMaxLength(SahamConsts.PhoneNumberMaxLength);
            b.Property(x => x.Template).IsRequired().HasMaxLength(128);
            b.Property(x => x.Payload).IsRequired().HasColumnType("jsonb");
            b.Property(x => x.ProviderMessageId).HasMaxLength(128);
            b.Property(x => x.LastError).HasMaxLength(Notification.ErrorMaxLength);
            // SRS: UK (event_key, channel, user_id) — never the same notification twice.
            b.HasIndex(x => new { x.EventKey, x.Channel, x.UserId }).IsUnique().AreNullsDistinct(false);
            b.HasIndex(x => new { x.Status, x.NextAttemptAt });
            // Phase 6: claimed messages whose worker died are found by their expired lease.
            b.HasIndex(x => x.LeaseUntil).HasFilter("\"Status\" = 'Processing'").HasDatabaseName("IX_AppNotifications_ExpiredLease");
            b.ToTable(t => t.HasCheckConstraint("CK_Notifications_Lease",
                "(\"Status\" = 'Processing') = (\"LeaseToken\" IS NOT NULL AND \"LeaseUntil\" IS NOT NULL)"));
            b.HasIndex(x => x.OrderId);
        });

        builder.Entity<UserDevice>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "UserDevices", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Token).IsRequired().HasMaxLength(UserDevice.TokenMaxLength);
            b.Property(x => x.Platform).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.HasIndex(x => x.Token).IsUnique(); // one device, one user
            b.HasIndex(x => x.UserId);
        });
    }
}
