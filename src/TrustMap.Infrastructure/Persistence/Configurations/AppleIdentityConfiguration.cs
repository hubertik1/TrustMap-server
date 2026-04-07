using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrustMap.Domain.Entities;

namespace TrustMap.Infrastructure.Persistence.Configurations;

internal sealed class AppleIdentityConfiguration : IEntityTypeConfiguration<AppleIdentity>
{
    public void Configure(EntityTypeBuilder<AppleIdentity> builder)
    {
        builder.ToTable("apple_identities");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Subject).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(320);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.LastLoginAt).IsRequired();

        builder.HasIndex(x => x.Subject).IsUnique();
        builder.HasIndex(x => x.UserId).IsUnique();

        builder.HasOne(x => x.User)
            .WithMany(x => x.AppleIdentities)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
