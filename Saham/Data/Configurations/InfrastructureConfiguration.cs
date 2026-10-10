using Microsoft.EntityFrameworkCore;
using Saham.Infrastructure.Idempotency;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Saham.Data.Configurations;

/* Cross-cutting technical tables. */
public static class InfrastructureConfiguration
{
    public static void ConfigureSahamInfrastructure(this ModelBuilder builder)
    {
        builder.Entity<IdempotencyRecord>(b =>
        {
            b.ToTable(SahamDbContext.DbTablePrefix + "IdempotencyRecords", SahamDbContext.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Scope).IsRequired().HasMaxLength(256);
            b.Property(x => x.Key).IsRequired().HasMaxLength(128);
            b.Property(x => x.RequestHash).IsRequired().HasMaxLength(128);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(SahamConsts.EnumMaxLength);
            b.Property(x => x.ResponseBody).HasColumnType("jsonb");
            b.HasIndex(x => new { x.UserId, x.Scope, x.Key }).IsUnique();
            b.HasIndex(x => x.CreationTime);
        });
    }
}
