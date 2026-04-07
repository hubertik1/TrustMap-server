using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrustMap.Domain.Entities;

namespace TrustMap.Infrastructure.Persistence.Configurations;

internal sealed class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> builder)
    {
        builder.ToTable("friendships");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PairKey).HasMaxLength(65).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.PairKey).IsUnique();
        builder.HasIndex(x => x.UserAId);
        builder.HasIndex(x => x.UserBId);

        builder.HasOne(x => x.UserA)
            .WithMany()
            .HasForeignKey(x => x.UserAId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.UserB)
            .WithMany()
            .HasForeignKey(x => x.UserBId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
