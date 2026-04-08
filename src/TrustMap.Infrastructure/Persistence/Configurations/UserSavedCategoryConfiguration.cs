using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrustMap.Domain.Entities;

namespace TrustMap.Infrastructure.Persistence.Configurations;

internal sealed class UserSavedCategoryConfiguration : IEntityTypeConfiguration<UserSavedCategory>
{
    public void Configure(EntityTypeBuilder<UserSavedCategory> builder)
    {
        builder.ToTable("user_saved_categories");

        builder.HasKey(x => new { x.UserId, x.CategoryId });

        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasOne(x => x.User)
            .WithMany(x => x.SavedCategories)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Category)
            .WithMany(x => x.SavedByUsers)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
