using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrustMap.Domain.Entities;

namespace TrustMap.Infrastructure.Persistence.Configurations;

internal sealed class UserCategoryMembershipConfiguration : IEntityTypeConfiguration<UserCategoryMembership>
{
    public void Configure(EntityTypeBuilder<UserCategoryMembership> builder)
    {
        builder.ToTable("user_category_memberships");

        builder.HasKey(x => new { x.UserId, x.CategoryId });

        builder.Property(x => x.Source).IsRequired();
        builder.Property(x => x.IsVisible).IsRequired();
        builder.Property(x => x.AddedAt).IsRequired();
        builder.Property(x => x.HiddenAt);
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => new { x.UserId, x.IsVisible });
        builder.HasIndex(x => x.CategoryId);

        builder.HasOne(x => x.User)
            .WithMany(x => x.CategoryMemberships)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Category)
            .WithMany(x => x.Memberships)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
