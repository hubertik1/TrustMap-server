using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TrustMap.Api.Common;
using TrustMap.Api.RateLimiting;
using TrustMap.Api.Services.Moderation;

namespace TrustMap.Api.Controllers;

[ApiController]
[Authorize]
[Route("reports")]
public sealed class ReportsController(ReportService reports) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicyNames.FriendRequests)]
    public async Task<IActionResult> Create([FromBody] CreateReportRequest request, CancellationToken cancellationToken)
    {
        var id = await reports.CreateAsync(User.GetRequiredUserId(), request.ReviewType, request.ReviewId, cancellationToken);
        return Accepted(new { id });
    }
}

public sealed class CreateReportRequest
{
    [Required, StringLength(20)] public string ReviewType { get; init; } = "";
    public Guid ReviewId { get; init; }
}
