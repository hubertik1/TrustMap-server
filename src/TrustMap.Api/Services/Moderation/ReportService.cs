using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Common;
using TrustMap.Domain.Entities;
using TrustMap.Infrastructure.Persistence;
using TrustMap.Infrastructure.Storage;

namespace TrustMap.Api.Services.Moderation;

public sealed class ReportService(
    TrustMapDbContext dbContext,
    PrivacyAccessService privacyAccess,
    IPhotoStorage storage,
    IOptions<ModerationOptions> options,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<Guid> CreateAsync(Guid reporterId, string reviewType, Guid reviewId, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
            throw new ApiException(HttpStatusCode.ServiceUnavailable, "Reporting unavailable", "Reporting is temporarily unavailable. Please try again later.", "reporting_unavailable");
        if (reviewType is not ("placeReview" or "dishReview"))
            throw ApiException.BadRequest("Unknown review type.", "report_type_invalid");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var existing = await dbContext.ContentReports.SingleOrDefaultAsync(x => x.ReporterUserId == reporterId
            && x.ReviewType == reviewType && x.ReviewId == reviewId, cancellationToken);
        if (existing is not null) return existing.Id;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (await dbContext.ContentReports.CountAsync(x => x.ReporterUserId == reporterId && x.CreatedAtUtc >= now.AddDays(-1), cancellationToken)
            >= options.Value.MaxReportsPerUserPerDay)
            throw ApiException.TooManyRequests("You have reached the daily report limit.", "report_limit");

        var social = await privacyAccess.GetViewerSocialAccessAsync(reporterId, cancellationToken);
        object snapshot;
        Guid authorId;
        List<Photo> photos;
        if (reviewType == "placeReview")
        {
            var review = await dbContext.PlaceReviews.AsNoTracking().Include(x => x.AuthorUser).Include(x => x.Photos)
                .Include(x => x.Place).ThenInclude(x => x.CategoryAssignments).ThenInclude(x => x.Category)
                .VisibleToViewer(reporterId, social.FriendIds, social.FriendOfFriendIds)
                .SingleOrDefaultAsync(x => x.Id == reviewId, cancellationToken)
                ?? throw ApiException.NotFound("Review not found.", "review_not_found");
            authorId = review.AuthorUserId;
            photos = review.Photos.ToList();
            snapshot = review.ToDto(reporterId, false);
        }
        else
        {
            var review = await dbContext.DishReviews.AsNoTracking().Include(x => x.AuthorUser).Include(x => x.Photos)
                .Include(x => x.Place).ThenInclude(x => x.CategoryAssignments).ThenInclude(x => x.Category)
                .VisibleToViewer(reporterId, social.FriendIds, social.FriendOfFriendIds)
                .SingleOrDefaultAsync(x => x.Id == reviewId, cancellationToken)
                ?? throw ApiException.NotFound("Review not found.", "review_not_found");
            authorId = review.AuthorUserId;
            photos = review.Photos.ToList();
            snapshot = review.ToDto(reporterId, false);
        }
        if (authorId == reporterId)
            throw ApiException.BadRequest("You cannot report your own review.", "report_self");

        var report = new ContentReport
        {
            Id = Guid.CreateVersion7(), ReporterUserId = reporterId, AuthorUserId = authorId,
            ReviewId = reviewId, ReviewType = reviewType,
            SnapshotJson = JsonSerializer.Serialize(snapshot, SnapshotOptions),
            CreatedAtUtc = now, NextDeliveryAtUtc = now
        };
        // Keep bounded, independent previews so the submitted evidence survives
        // edits/deletion. Full source image metadata remains in the JSON snapshot.
        foreach (var photo in photos)
        {
            var file = await storage.OpenReadAsync(photo.ThumbnailStorageKey ?? photo.StorageKey, photo.ContentType, cancellationToken);
            if (file is null) continue;
            await using var stream = file.Stream;
            using var image = await Image.LoadAsync(stream, cancellationToken);
            image.Mutate(x => x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(400, 400) }));
            using var preview = new MemoryStream();
            await image.SaveAsJpegAsync(preview, new JpegEncoder { Quality = 65 }, cancellationToken);
            report.Photos.Add(new ReportPhoto
            {
                Id = Guid.CreateVersion7(), ContentReportId = report.Id,
                SourcePhotoId = photo.Id, PreviewJpeg = preview.ToArray()
            });
        }
        dbContext.ContentReports.Add(report);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return report.Id;
    }
}
