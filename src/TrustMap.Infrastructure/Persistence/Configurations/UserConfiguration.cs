using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;

namespace TrustMap.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "CK_users_review_visibility",
                EnumCheckConstraintSql.IntegerValues<VisibilityLevel>("ReviewVisibility"));
            tableBuilder.HasCheckConstraint(
                "CK_users_friend_list_visibility",
                EnumCheckConstraintSql.IntegerValues<VisibilityLevel>("FriendListVisibility"));
            tableBuilder.HasCheckConstraint(
                "CK_users_profile_visibility",
                EnumCheckConstraintSql.IntegerValues<VisibilityLevel>("ProfileVisibility"));
            tableBuilder.HasCheckConstraint(
                "CK_users_profile_picture_visibility",
                EnumCheckConstraintSql.IntegerValues<VisibilityLevel>("ProfilePictureVisibility"));
            tableBuilder.HasCheckConstraint(
                "CK_users_status",
                EnumCheckConstraintSql.IntegerValues<UserAccountStatus>("Status"));
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Handle).HasMaxLength(32).IsRequired();
        builder.Property(x => x.NormalizedHandle).HasMaxLength(32).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NormalizedDisplayName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Bio).HasMaxLength(500);
        builder.Property(x => x.AvatarUrl).HasMaxLength(500);
        builder.Property(x => x.Email).HasMaxLength(320);
        builder.Property(x => x.ReviewVisibility).IsRequired().HasDefaultValue(VisibilityLevel.Friends);
        builder.Property(x => x.FriendListVisibility).IsRequired().HasDefaultValue(VisibilityLevel.Friends);
        builder.Property(x => x.ProfileVisibility).IsRequired().HasDefaultValue(VisibilityLevel.Public);
        builder.Property(x => x.ProfilePictureVisibility).IsRequired().HasDefaultValue(VisibilityLevel.Public);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => x.NormalizedHandle).IsUnique();
        builder.HasIndex(x => x.NormalizedDisplayName);
    }
}
