using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrustMap.Domain.Entities;

namespace TrustMap.Infrastructure.Persistence.Configurations;

public sealed class ContentReportConfiguration : IEntityTypeConfiguration<ContentReport>
{
    public void Configure(EntityTypeBuilder<ContentReport> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ReviewType).HasMaxLength(20);
        builder.Property(x => x.Resolution).HasMaxLength(20);
        builder.HasIndex(x => new { x.ReporterUserId, x.ReviewType, x.ReviewId }).IsUnique();
        builder.HasIndex(x => new { x.EmailProcessedAtUtc, x.NextDeliveryAtUtc });
        // Evidence has a bounded retention period and must survive edits/deletion
        // of its source. It deliberately has no FK to reviews or accounts.
        builder.HasMany(x => x.Photos).WithOne().HasForeignKey(x => x.ContentReportId).OnDelete(DeleteBehavior.Cascade);
    }
}
