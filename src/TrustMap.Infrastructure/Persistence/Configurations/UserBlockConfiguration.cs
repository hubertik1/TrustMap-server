using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrustMap.Domain.Entities;

namespace TrustMap.Infrastructure.Persistence.Configurations;

public sealed class UserBlockConfiguration : IEntityTypeConfiguration<UserBlock>
{
    public void Configure(EntityTypeBuilder<UserBlock> builder)
    {
        builder.HasKey(x => new { x.BlockerUserId, x.BlockedUserId });
        builder.HasIndex(x => x.BlockedUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.BlockerUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.BlockedUserId).OnDelete(DeleteBehavior.Cascade);
    }
}
