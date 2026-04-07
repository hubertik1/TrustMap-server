using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrustMap.Domain.Entities;

namespace TrustMap.Infrastructure.Persistence.Configurations;

internal sealed class PlaceReviewConfiguration : IEntityTypeConfiguration<PlaceReview>
{
    public void Configure(EntityTypeBuilder<PlaceReview> builder)
    {
        builder.ToTable("place_reviews");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).HasMaxLength(160);
        builder.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => new { x.PlaceId, x.AuthorUserId }).IsUnique();
        builder.HasIndex(x => new { x.AuthorUserId, x.UpdatedAt });
        builder.HasIndex(x => new { x.PlaceId, x.UpdatedAt });
        builder.HasIndex(x => new { x.Visibility, x.UpdatedAt });

        builder.HasOne(x => x.Place)
            .WithMany(x => x.PlaceReviews)
            .HasForeignKey(x => x.PlaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.AuthorUser)
            .WithMany(x => x.PlaceReviews)
            .HasForeignKey(x => x.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
