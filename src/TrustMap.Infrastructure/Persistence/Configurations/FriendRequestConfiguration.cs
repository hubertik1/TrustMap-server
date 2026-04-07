using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrustMap.Domain.Entities;

namespace TrustMap.Infrastructure.Persistence.Configurations;

internal sealed class FriendRequestConfiguration : IEntityTypeConfiguration<FriendRequest>
{
    public void Configure(EntityTypeBuilder<FriendRequest> builder)
    {
        builder.ToTable("friend_requests");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ActiveRequestKey).HasMaxLength(65);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => x.ActiveRequestKey).IsUnique();
        builder.HasIndex(x => new { x.ReceiverUserId, x.Status, x.CreatedAt });
        builder.HasIndex(x => new { x.SenderUserId, x.Status, x.CreatedAt });

        builder.HasOne(x => x.SenderUser)
            .WithMany()
            .HasForeignKey(x => x.SenderUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReceiverUser)
            .WithMany()
            .HasForeignKey(x => x.ReceiverUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
