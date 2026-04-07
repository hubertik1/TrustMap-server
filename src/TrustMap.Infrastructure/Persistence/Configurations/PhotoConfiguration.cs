using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrustMap.Domain.Entities;

namespace TrustMap.Infrastructure.Persistence.Configurations;

internal sealed class PhotoConfiguration : IEntityTypeConfiguration<Photo>
{
    public void Configure(EntityTypeBuilder<Photo> builder)
    {
        builder.ToTable("photos");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.StorageProvider).HasMaxLength(40).IsRequired();
        builder.Property(x => x.StorageKey).HasMaxLength(500).IsRequired();
        builder.Property(x => x.PublicUrl).HasMaxLength(500).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.PlaceId);
        builder.HasIndex(x => x.PlaceReviewId);
        builder.HasIndex(x => x.DishReviewId);
        builder.HasIndex(x => new { x.OwnerUserId, x.CreatedAt });

        builder.ToTable(tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_photos_single_parent",
                "((PlaceReviewId IS NOT NULL AND DishReviewId IS NULL) OR (PlaceReviewId IS NULL AND DishReviewId IS NOT NULL))");
        });

        builder.HasOne(x => x.OwnerUser)
            .WithMany(x => x.Photos)
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Place)
            .WithMany(x => x.Photos)
            .HasForeignKey(x => x.PlaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.PlaceReview)
            .WithMany(x => x.Photos)
            .HasForeignKey(x => x.PlaceReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.DishReview)
            .WithMany(x => x.Photos)
            .HasForeignKey(x => x.DishReviewId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
