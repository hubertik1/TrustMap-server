using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrustMap.Api.Common;
using TrustMap.Api.RateLimiting;
using TrustMap.Api.Services.Moderation;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Controllers;

[AllowAnonymous]
[Route("moderation")]
[EnableRateLimiting(RateLimitPolicyNames.Auth)]
public sealed class ModerationController(
    TrustMapDbContext dbContext, ModerationLinks links, IAntiforgery antiforgery,
    IOptions<ModerationOptions> options, TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("{moderationAction}")]
    public async Task<IActionResult> Confirm([FromRoute(Name = "moderationAction")] string action, [FromQuery] string token, CancellationToken cancellationToken)
    {
        SecureHeaders();
        var id = links.Read(action, token ?? "");
        var report = await dbContext.ContentReports.Include(x => x.Photos).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiException.NotFound("Report not found.");
        if (report.ResolvedAtUtc != null)
            return Content(ModerationPage.Layout("<h1>Zgłoszenie zostało już rozpatrzone.</h1>"), "text/html", Encoding.UTF8);
        var csrf = antiforgery.GetAndStoreTokens(HttpContext);
        return Content(ModerationPage.Confirm(report, action, token!, csrf.FormFieldName, csrf.RequestToken!), "text/html", Encoding.UTF8);
    }

    [HttpPost("{moderationAction}")]
    [RequestSizeLimit(16_384)]
    public async Task<IActionResult> Execute([FromRoute(Name = "moderationAction")] string action, [FromForm] string token, [FromForm] string? adminPassword, CancellationToken cancellationToken)
    {
        SecureHeaders();
        try { await antiforgery.ValidateRequestAsync(HttpContext); }
        catch (AntiforgeryValidationException) { return BadRequest("Refresh the confirmation page and try again."); }
        var id = links.Read(action, token ?? "");
        if (action == "ban" && string.IsNullOrEmpty(options.Value.AdminPasswordSha256))
            return StatusCode(503, "Set the administrator password on the server before banning accounts.");
        if (action == "ban" && !ValidPassword(adminPassword))
            return StatusCode(403, "Incorrect administrator password. Return to the confirmation page and try again.");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var report = await dbContext.ContentReports.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiException.NotFound("Report not found.");
        var now = timeProvider.GetUtcNow().UtcDateTime;
        // Atomic claim: all three action links for this report become unusable.
        var claimed = await dbContext.ContentReports.Where(x => x.Id == id && x.ResolvedAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(r => r.ResolvedAtUtc, now).SetProperty(r => r.Resolution, action), cancellationToken);
        if (claimed == 0) return Conflict("This report has already been resolved.");

        if (action == "remove")
        {
            if (report.ReviewType == "placeReview")
                await dbContext.PlaceReviews.IgnoreQueryFilters().Where(x => x.Id == report.ReviewId && x.AuthorUserId == report.AuthorUserId)
                    .ExecuteUpdateAsync(x => x.SetProperty(r => r.IsRemovedByModeration, true), cancellationToken);
            else
                await dbContext.DishReviews.IgnoreQueryFilters().Where(x => x.Id == report.ReviewId && x.AuthorUserId == report.AuthorUserId)
                    .ExecuteUpdateAsync(x => x.SetProperty(r => r.IsRemovedByModeration, true), cancellationToken);
            await dbContext.ContentReports.Where(x => x.ReviewType == report.ReviewType && x.ReviewId == report.ReviewId && x.ResolvedAtUtc == null)
                .ExecuteUpdateAsync(x => x.SetProperty(r => r.ResolvedAtUtc, now).SetProperty(r => r.Resolution, action), cancellationToken);
        }
        if (action == "ban")
        {
            var author = await dbContext.Users.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == report.AuthorUserId, cancellationToken);
            if (author?.Status == UserAccountStatus.Active) author.SetStatus(UserAccountStatus.Blocked);
            await dbContext.RefreshTokens.Where(x => x.UserId == report.AuthorUserId).ExecuteDeleteAsync(cancellationToken);
            await dbContext.DeviceTokens.Where(x => x.UserId == report.AuthorUserId).ExecuteDeleteAsync(cancellationToken);
            await dbContext.ContentReports.Where(x => x.AuthorUserId == report.AuthorUserId && x.ResolvedAtUtc == null)
                .ExecuteUpdateAsync(x => x.SetProperty(r => r.ResolvedAtUtc, now).SetProperty(r => r.Resolution, action), cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Content(ModerationPage.Layout("<h1>Gotowe.</h1><p>Decyzja została zapisana. Możesz zamknąć tę stronę.</p>"), "text/html", Encoding.UTF8);
    }

    private bool ValidPassword(string? password) => password is { Length: > 0 and <= 256 }
        && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(password)), Convert.FromHexString(options.Value.AdminPasswordSha256));

    private void SecureHeaders()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; img-src data:; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";
    }
}
