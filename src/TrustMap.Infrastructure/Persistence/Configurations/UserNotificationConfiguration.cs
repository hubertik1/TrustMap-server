using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;

namespace TrustMap.Infrastructure.Persistence.Configurations;

internal sealed class UserNotificationConfiguration : IEntityTypeConfiguration<UserNotification>
{
    public void Configure(EntityTypeBuilder<UserNotification> builder)
    {
        builder.ToTable("notifications", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "CK_notifications_references_match_type",
                $"""
                ("Type" = {(int)NotificationType.PlaceReviewAdded}
                    AND "PlaceId" IS NOT NULL
                    AND "PlaceReviewId" IS NOT NULL
                    AND "DishReviewId" IS NULL
                    AND "FriendRequestId" IS NULL)
                OR ("Type" = {(int)NotificationType.DishReviewAdded}
                    AND "PlaceId" IS NOT NULL
                    AND "PlaceReviewId" IS NULL
                    AND "DishReviewId" IS NOT NULL
                    AND "FriendRequestId" IS NULL)
                OR ("Type" = {(int)NotificationType.FriendRequestSent}
                    AND "PlaceId" IS NULL
                    AND "PlaceReviewId" IS NULL
                    AND "DishReviewId" IS NULL
                    AND "FriendRequestId" IS NOT NULL)
                """);
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.DeduplicationKey).HasMaxLength(160).IsRequired();

        builder.HasIndex(x => x.DeduplicationKey).IsUnique();
        builder.HasIndex(x => new { x.RecipientUserId, x.CreatedAt });
        builder.HasIndex(x => new { x.RecipientUserId, x.ReadAt, x.CreatedAt });
        builder.HasIndex(x => new { x.ActorUserId, x.CreatedAt });
        builder.HasIndex(x => x.PlaceReviewId);
        builder.HasIndex(x => x.DishReviewId);
        builder.HasIndex(x => x.FriendRequestId);

        builder.HasOne(x => x.RecipientUser)
            .WithMany()
            .HasForeignKey(x => x.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ActorUser)
            .WithMany()
            .HasForeignKey(x => x.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Place)
            .WithMany()
            .HasForeignKey(x => x.PlaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.PlaceReview)
            .WithMany()
            .HasForeignKey(x => x.PlaceReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.DishReview)
            .WithMany()
            .HasForeignKey(x => x.DishReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.FriendRequest)
            .WithMany()
            .HasForeignKey(x => x.FriendRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
