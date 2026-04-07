using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrustMap.Domain.Entities;

namespace TrustMap.Infrastructure.Persistence.Configurations;

internal sealed class DishReviewConfiguration : IEntityTypeConfiguration<DishReview>
{
    public void Configure(EntityTypeBuilder<DishReview> builder)
    {
        builder.ToTable("dish_reviews");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DishName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(3);
        builder.Property(x => x.PriceAmount).HasPrecision(10, 2);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => new { x.PlaceId, x.UpdatedAt });
        builder.HasIndex(x => new { x.AuthorUserId, x.UpdatedAt });
        builder.HasIndex(x => new { x.Visibility, x.UpdatedAt });
        builder.HasIndex(x => x.PlaceReviewId);

        builder.HasOne(x => x.Place)
            .WithMany(x => x.DishReviews)
            .HasForeignKey(x => x.PlaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.AuthorUser)
            .WithMany(x => x.DishReviews)
            .HasForeignKey(x => x.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.PlaceReview)
            .WithMany(x => x.DishReviews)
            .HasForeignKey(x => x.PlaceReviewId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
