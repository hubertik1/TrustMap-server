using Microsoft.EntityFrameworkCore;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;

namespace TrustMap.Infrastructure.Persistence;

public sealed class TrustMapDbContext(DbContextOptions<TrustMapDbContext> options) : DbContext(options)
{
    // Set once by authenticated request middleware. Background jobs have no viewer.
    public Guid? ViewerUserId { get; set; }

    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();

    public DbSet<ContentReport> ContentReports => Set<ContentReport>();

    public DbSet<User> Users => Set<User>();

    public DbSet<AppleIdentity> AppleIdentities => Set<AppleIdentity>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();

    public DbSet<Friendship> Friendships => Set<Friendship>();

    public DbSet<Place> Places => Set<Place>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<PlaceCategoryAssignment> PlaceCategoryAssignments => Set<PlaceCategoryAssignment>();

    public DbSet<UserCategoryMembership> UserCategoryMemberships => Set<UserCategoryMembership>();

    public DbSet<PlaceReview> PlaceReviews => Set<PlaceReview>();

    public DbSet<DishReview> DishReviews => Set<DishReview>();

    public DbSet<Photo> Photos => Set<Photo>();

    public DbSet<UserNotification> Notifications => Set<UserNotification>();

    public DbSet<UserDeviceToken> DeviceTokens => Set<UserDeviceToken>();

    public DbSet<QuotaUsage> QuotaUsages => Set<QuotaUsage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TrustMapDbContext).Assembly);

        // Apply at the database boundary so direct lookups, media and aggregate
        // projections cannot bypass a block by forgetting a controller predicate.
        modelBuilder.Entity<User>().HasQueryFilter(x => ViewerUserId == null
            || !UserBlocks.Any(b =>
                (b.BlockerUserId == ViewerUserId && b.BlockedUserId == x.Id)
                || (b.BlockedUserId == ViewerUserId && b.BlockerUserId == x.Id)));
        modelBuilder.Entity<Category>().HasQueryFilter(x => x.OwnerUserId == null
            || (x.OwnerUser!.Status == UserAccountStatus.Active
                && (ViewerUserId == null || !UserBlocks.Any(b =>
                    (b.BlockerUserId == ViewerUserId && b.BlockedUserId == x.OwnerUserId)
                    || (b.BlockedUserId == ViewerUserId && b.BlockerUserId == x.OwnerUserId)))));
        modelBuilder.Entity<PlaceCategoryAssignment>().HasQueryFilter(x => ViewerUserId == null
            || !UserBlocks.Any(b =>
                (b.BlockerUserId == ViewerUserId && b.BlockedUserId == x.AssignedByUserId)
                || (b.BlockedUserId == ViewerUserId && b.BlockerUserId == x.AssignedByUserId)));
        modelBuilder.Entity<PlaceReview>().HasQueryFilter(x => !x.IsRemovedByModeration
            && x.AuthorUser.Status == UserAccountStatus.Active
            && (ViewerUserId == null || !UserBlocks.Any(b =>
                (b.BlockerUserId == ViewerUserId && b.BlockedUserId == x.AuthorUserId)
                || (b.BlockedUserId == ViewerUserId && b.BlockerUserId == x.AuthorUserId))));
        modelBuilder.Entity<DishReview>().HasQueryFilter(x => !x.IsRemovedByModeration
            && x.AuthorUser.Status == UserAccountStatus.Active
            && (ViewerUserId == null || !UserBlocks.Any(b =>
                (b.BlockerUserId == ViewerUserId && b.BlockedUserId == x.AuthorUserId)
                || (b.BlockedUserId == ViewerUserId && b.BlockerUserId == x.AuthorUserId))));
        modelBuilder.Entity<Photo>().HasQueryFilter(x => x.OwnerUser.Status == UserAccountStatus.Active
            && (x.PlaceReviewId == null || !x.PlaceReview!.IsRemovedByModeration)
            && (x.DishReviewId == null || !x.DishReview!.IsRemovedByModeration)
            && (ViewerUserId == null || !UserBlocks.Any(b =>
                (b.BlockerUserId == ViewerUserId && b.BlockedUserId == x.OwnerUserId)
                || (b.BlockedUserId == ViewerUserId && b.BlockerUserId == x.OwnerUserId))));
        modelBuilder.Entity<Friendship>().HasQueryFilter(x => ViewerUserId == null
            || !UserBlocks.Any(b =>
                (b.BlockerUserId == ViewerUserId && (b.BlockedUserId == x.UserAId || b.BlockedUserId == x.UserBId))
                || (b.BlockedUserId == ViewerUserId && (b.BlockerUserId == x.UserAId || b.BlockerUserId == x.UserBId))));
        modelBuilder.Entity<FriendRequest>().HasQueryFilter(x => ViewerUserId == null
            || !UserBlocks.Any(b =>
                (b.BlockerUserId == ViewerUserId && (b.BlockedUserId == x.SenderUserId || b.BlockedUserId == x.ReceiverUserId))
                || (b.BlockedUserId == ViewerUserId && (b.BlockerUserId == x.SenderUserId || b.BlockerUserId == x.ReceiverUserId))));
        modelBuilder.Entity<UserNotification>().HasQueryFilter(x => x.ActorUser.Status == UserAccountStatus.Active
            && x.RecipientUser.Status == UserAccountStatus.Active
            && (x.PlaceReviewId == null || !x.PlaceReview!.IsRemovedByModeration)
            && (x.DishReviewId == null || !x.DishReview!.IsRemovedByModeration)
            && !UserBlocks.Any(b =>
                (b.BlockerUserId == x.ActorUserId && b.BlockedUserId == x.RecipientUserId)
                || (b.BlockerUserId == x.RecipientUserId && b.BlockedUserId == x.ActorUserId)));
    }
}
