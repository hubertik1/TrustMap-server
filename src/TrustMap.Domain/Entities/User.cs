using TrustMap.Domain.Common;
using TrustMap.Domain.Enums;

namespace TrustMap.Domain.Entities;

public sealed class User
{
    private User()
    {
    }

    public User(Guid id, string handle, string displayName, string? email = null)
    {
        Id = id;
        CreatedAt = DateTime.UtcNow;
        Status = UserAccountStatus.Active;
        UpdateProfile(handle, displayName, null, null);
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    }

    public Guid Id { get; private set; }

    public string Handle { get; private set; } = null!;

    public string NormalizedHandle { get; private set; } = null!;

    public string DisplayName { get; private set; } = null!;

    public string NormalizedDisplayName { get; private set; } = null!;

    public string? Bio { get; private set; }

    public string? AvatarUrl { get; private set; }

    public string? Email { get; private set; }

    public VisibilityLevel ReviewVisibility { get; private set; } = VisibilityLevel.Friends;

    public VisibilityLevel FriendListVisibility { get; private set; } = VisibilityLevel.Friends;

    public VisibilityLevel ProfileVisibility { get; private set; } = VisibilityLevel.Public;

    public VisibilityLevel ProfilePictureVisibility { get; private set; } = VisibilityLevel.Public;

    public UserAccountStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    public ICollection<AppleIdentity> AppleIdentities { get; } = new List<AppleIdentity>();

    public ICollection<RefreshToken> RefreshTokens { get; } = new List<RefreshToken>();

    public ICollection<PlaceReview> PlaceReviews { get; } = new List<PlaceReview>();

    public ICollection<DishReview> DishReviews { get; } = new List<DishReview>();

    public ICollection<Photo> Photos { get; } = new List<Photo>();

    public ICollection<Category> OwnedCategories { get; } = new List<Category>();

    public ICollection<PlaceCategoryAssignment> PlaceCategoryAssignments { get; } = new List<PlaceCategoryAssignment>();

    public ICollection<UserCategoryMembership> CategoryMemberships { get; } = new List<UserCategoryMembership>();

    public void UpdateProfile(string handle, string displayName, string? bio, string? avatarUrl)
    {
        Handle = TextNormalizer.NormalizeHandle(handle);
        if (string.IsNullOrWhiteSpace(Handle))
        {
            throw new InvalidOperationException("Handle cannot be empty.");
        }

        DisplayName = displayName.Trim();
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            throw new InvalidOperationException("Display name cannot be empty.");
        }

        NormalizedHandle = TextNormalizer.NormalizeForLookup(Handle);
        NormalizedDisplayName = TextNormalizer.NormalizeForLookup(DisplayName);
        Bio = string.IsNullOrWhiteSpace(bio) ? null : bio.Trim();
        AvatarUrl = string.IsNullOrWhiteSpace(avatarUrl) ? null : avatarUrl.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateEmail(string? email)
    {
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdatePrivacySettings(
        VisibilityLevel reviewVisibility,
        VisibilityLevel friendListVisibility,
        VisibilityLevel profileVisibility,
        VisibilityLevel profilePictureVisibility)
    {
        EnsureValidPrivacySettings(reviewVisibility, friendListVisibility, profileVisibility, profilePictureVisibility);

        ReviewVisibility = reviewVisibility;
        FriendListVisibility = friendListVisibility;
        ProfileVisibility = profileVisibility;
        ProfilePictureVisibility = profilePictureVisibility;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetStatus(UserAccountStatus status)
    {
        EnumGuard.EnsureDefined(status, nameof(status));
        Status = status;
        UpdatedAt = DateTime.UtcNow;
    }

    private static void EnsureValidPrivacySettings(
        VisibilityLevel reviewVisibility,
        VisibilityLevel friendListVisibility,
        VisibilityLevel profileVisibility,
        VisibilityLevel profilePictureVisibility)
    {
        EnumGuard.EnsureDefined(reviewVisibility, nameof(reviewVisibility));
        EnumGuard.EnsureDefined(friendListVisibility, nameof(friendListVisibility));
        EnumGuard.EnsureDefined(profileVisibility, nameof(profileVisibility));
        EnumGuard.EnsureDefined(profilePictureVisibility, nameof(profilePictureVisibility));

        if (reviewVisibility == VisibilityLevel.Private)
        {
            throw new InvalidOperationException("Review visibility cannot be private.");
        }

        if (profileVisibility == VisibilityLevel.Private)
        {
            throw new InvalidOperationException("Profile visibility cannot be private.");
        }

        if (profilePictureVisibility == VisibilityLevel.Private)
        {
            throw new InvalidOperationException("Profile picture visibility cannot be private.");
        }
    }

}
