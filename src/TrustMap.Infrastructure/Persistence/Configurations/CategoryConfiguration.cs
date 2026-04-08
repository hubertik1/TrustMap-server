using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrustMap.Domain.Entities;

namespace TrustMap.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.IconName).HasMaxLength(80);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => new { x.OwnerUserId, x.NormalizedName }).IsUnique();
        builder.HasIndex(x => new { x.IsDefault, x.NormalizedName });

        builder.HasOne(x => x.OwnerUser)
            .WithMany(x => x.OwnedCategories)
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
