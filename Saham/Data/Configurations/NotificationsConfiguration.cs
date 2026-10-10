using Microsoft.EntityFrameworkCore;
using Saham.Entities.Notifications;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Saham.Data.Configurations;

/* M10 Notifications. */
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
            b.HasIndex(x => new { x.PhoneNumber, x.CreationTime });
            b.HasIndex(x => x.CreationTime);
        });
    }
}
