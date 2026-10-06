using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrustMap.Domain.Entities;

namespace TrustMap.Infrastructure.Persistence.Configurations;

internal sealed class AccountDeletionJobConfiguration : IEntityTypeConfiguration<AccountDeletionJob>
{
    public void Configure(EntityTypeBuilder<AccountDeletionJob> builder)
    {
        builder.ToTable("account_deletion_jobs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Payload).HasMaxLength(16384).IsRequired();
        builder.Property(x => x.ClientId).HasMaxLength(255);
        builder.HasIndex(x => x.NextAttemptAtUtc);
    }
}
