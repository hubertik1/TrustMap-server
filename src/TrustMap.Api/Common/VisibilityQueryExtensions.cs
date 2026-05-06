using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;

namespace TrustMap.Api.Common;

public static class VisibilityQueryExtensions
{
    public static IQueryable<PlaceReview> VisibleToViewer(
        this IQueryable<PlaceReview> query,
        Guid viewerUserId,
        IReadOnlyCollection<Guid> friendIds,
        IReadOnlyCollection<Guid> friendOfFriendIds)
    {
        return query.Where(x =>
            x.AuthorUserId == viewerUserId
            || x.Visibility == VisibilityLevel.Public
            || (x.Visibility == VisibilityLevel.Friends && friendIds.Contains(x.AuthorUserId))
            || (x.Visibility == VisibilityLevel.FriendsOfFriends
                && (friendIds.Contains(x.AuthorUserId) || friendOfFriendIds.Contains(x.AuthorUserId))));
    }

    public static IQueryable<DishReview> VisibleToViewer(
        this IQueryable<DishReview> query,
        Guid viewerUserId,
        IReadOnlyCollection<Guid> friendIds,
        IReadOnlyCollection<Guid> friendOfFriendIds)
    {
        return query.Where(x =>
            x.AuthorUserId == viewerUserId
            || x.Visibility == VisibilityLevel.Public
            || (x.Visibility == VisibilityLevel.Friends && friendIds.Contains(x.AuthorUserId))
            || (x.Visibility == VisibilityLevel.FriendsOfFriends
                && (friendIds.Contains(x.AuthorUserId) || friendOfFriendIds.Contains(x.AuthorUserId))));
    }

    public static IQueryable<PlaceReview> VisibleInSocialScope(
        this IQueryable<PlaceReview> query,
        Guid viewerUserId,
        IReadOnlyCollection<Guid> friendIds,
        IReadOnlyCollection<Guid> friendOfFriendIds)
    {
        var authorIds = friendIds.Count == 0
            ? new List<Guid> { viewerUserId }
            : friendIds.Append(viewerUserId).ToList();

        return query.Where(x => authorIds.Contains(x.AuthorUserId))
            .VisibleToViewer(viewerUserId, friendIds, friendOfFriendIds);
    }

    public static IQueryable<DishReview> VisibleInSocialScope(
        this IQueryable<DishReview> query,
        Guid viewerUserId,
        IReadOnlyCollection<Guid> friendIds,
        IReadOnlyCollection<Guid> friendOfFriendIds)
    {
        var authorIds = friendIds.Count == 0
            ? new List<Guid> { viewerUserId }
            : friendIds.Append(viewerUserId).ToList();

        return query.Where(x => authorIds.Contains(x.AuthorUserId))
            .VisibleToViewer(viewerUserId, friendIds, friendOfFriendIds);
    }
}
