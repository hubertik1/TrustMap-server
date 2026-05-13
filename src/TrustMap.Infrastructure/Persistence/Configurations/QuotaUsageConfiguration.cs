using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrustMap.Domain.Entities;

namespace TrustMap.Infrastructure.Persistence.Configurations;

internal sealed class QuotaUsageConfiguration : IEntityTypeConfiguration<QuotaUsage>
{
    public void Configure(EntityTypeBuilder<QuotaUsage> builder)
    {
        builder.ToTable("quota_usages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Scope).HasMaxLength(24).IsRequired();
        builder.Property(x => x.SubjectKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.QuotaName).HasMaxLength(80).IsRequired();
        builder.Property(x => x.PeriodStartUtc).IsRequired();
        builder.Property(x => x.PeriodEndUtc).IsRequired();
        builder.Property(x => x.Count).IsRequired();
        builder.Property(x => x.Bytes).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.Scope, x.SubjectKey, x.QuotaName, x.PeriodStartUtc }).IsUnique();
        builder.HasIndex(x => x.PeriodEndUtc);
    }
}
