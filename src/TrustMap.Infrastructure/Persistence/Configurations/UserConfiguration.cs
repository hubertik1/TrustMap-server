using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrustMap.Domain.Entities;

namespace TrustMap.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Handle).HasMaxLength(32).IsRequired();
        builder.Property(x => x.NormalizedHandle).HasMaxLength(32).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NormalizedDisplayName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Bio).HasMaxLength(500);
        builder.Property(x => x.AvatarUrl).HasMaxLength(500);
        builder.Property(x => x.Email).HasMaxLength(320);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => x.NormalizedHandle).IsUnique();
        builder.HasIndex(x => x.NormalizedDisplayName);
    }
}
