using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Users;
using TrustMap.Api.Services;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Controllers;

[ApiController]
[Authorize]
[Route("me")]
public sealed class MeController(
    TrustMapDbContext dbContext,
    HandleGenerator handleGenerator,
    ProfileQueryService profileQueryService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    public Task<UserProfileDto> Get(CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        return profileQueryService.GetProfileAsync(currentUserId, currentUserId, cancellationToken);
    }

    [HttpPatch]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserProfileDto>> Patch(
        [FromBody] UpdateMeRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == currentUserId, cancellationToken)
            ?? throw ApiException.NotFound("User not found.", "user_not_found");

        var handle = await handleGenerator.GenerateUpdatedHandleAsync(
            request.Handle,
            user.Handle,
            request.DisplayName,
            user.Email,
            currentUserId,
            cancellationToken);

        user.UpdateProfile(handle, request.DisplayName, request.Bio, request.AvatarUrl);
        await dbContext.SaveChangesAsync(cancellationToken);

        var profile = await profileQueryService.GetProfileAsync(currentUserId, currentUserId, cancellationToken);
        return Ok(profile);
    }
}
