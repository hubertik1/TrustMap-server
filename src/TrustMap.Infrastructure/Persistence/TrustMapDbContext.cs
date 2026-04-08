using Microsoft.EntityFrameworkCore;
using TrustMap.Domain.Entities;

namespace TrustMap.Infrastructure.Persistence;

public sealed class TrustMapDbContext(DbContextOptions<TrustMapDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<AppleIdentity> AppleIdentities => Set<AppleIdentity>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<FriendRequest> FriendRequests => Set<FriendRequest>();

    public DbSet<Friendship> Friendships => Set<Friendship>();

    public DbSet<Place> Places => Set<Place>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<PlaceCategoryAssignment> PlaceCategoryAssignments => Set<PlaceCategoryAssignment>();

    public DbSet<UserSavedCategory> UserSavedCategories => Set<UserSavedCategory>();

    public DbSet<PlaceReview> PlaceReviews => Set<PlaceReview>();

    public DbSet<DishReview> DishReviews => Set<DishReview>();

    public DbSet<Photo> Photos => Set<Photo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TrustMapDbContext).Assembly);
    }
}
