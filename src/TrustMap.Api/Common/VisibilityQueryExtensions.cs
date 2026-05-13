using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;

namespace TrustMap.Api.Common;

public static class VisibilityQueryExtensions
{
    public static IQueryable<Place> VisibleToViewer(
        this IQueryable<Place> query,
        Guid viewerUserId,
        IReadOnlyCollection<Guid> friendIds,
        IReadOnlyCollection<Guid> friendOfFriendIds)
    {
        return query.Where(x =>
            x.IsPubliclyListed
            || x.SourceType == PlaceSourceType.ProviderVenue
            || x.SourceType == PlaceSourceType.ApprovedPublic
            || (x.SourceType == PlaceSourceType.CustomPin && x.CreatedByUserId == viewerUserId)
            || x.PlaceReviews.Any(review =>
                review.AuthorUserId == viewerUserId
                || review.Visibility == VisibilityLevel.Public
                || (review.Visibility == VisibilityLevel.Friends && friendIds.Contains(review.AuthorUserId))
                || (review.Visibility == VisibilityLevel.FriendsOfFriends
                    && (friendIds.Contains(review.AuthorUserId) || friendOfFriendIds.Contains(review.AuthorUserId))))
            || x.DishReviews.Any(review =>
                review.AuthorUserId == viewerUserId
                || review.Visibility == VisibilityLevel.Public
                || (review.Visibility == VisibilityLevel.Friends && friendIds.Contains(review.AuthorUserId))
                || (review.Visibility == VisibilityLevel.FriendsOfFriends
                    && (friendIds.Contains(review.AuthorUserId) || friendOfFriendIds.Contains(review.AuthorUserId)))));
    }

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
