using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrustMap.Domain.Entities;

namespace TrustMap.Infrastructure.Persistence.Configurations;

internal sealed class UserDeviceTokenConfiguration : IEntityTypeConfiguration<UserDeviceToken>
{
    public void Configure(EntityTypeBuilder<UserDeviceToken> builder)
    {
        builder.ToTable("user_device_tokens");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Token).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Environment).HasMaxLength(32).IsRequired();
        builder.Property(x => x.AppVersion).HasMaxLength(64);
        builder.Property(x => x.BuildNumber).HasMaxLength(64);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => new { x.Token, x.Platform, x.Environment }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.RevokedAt });

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
