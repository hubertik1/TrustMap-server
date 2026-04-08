using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrustMap.Domain.Entities;

namespace TrustMap.Infrastructure.Persistence.Configurations;

internal sealed class PlaceCategoryAssignmentConfiguration : IEntityTypeConfiguration<PlaceCategoryAssignment>
{
    public void Configure(EntityTypeBuilder<PlaceCategoryAssignment> builder)
    {
        builder.ToTable("place_category_assignments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => new { x.PlaceId, x.AssignedByUserId }).IsUnique();
        builder.HasIndex(x => new { x.CategoryId, x.PlaceId });

        builder.HasOne(x => x.Place)
            .WithMany(x => x.CategoryAssignments)
            .HasForeignKey(x => x.PlaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Category)
            .WithMany(x => x.PlaceAssignments)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.AssignedByUser)
            .WithMany(x => x.PlaceCategoryAssignments)
            .HasForeignKey(x => x.AssignedByUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
