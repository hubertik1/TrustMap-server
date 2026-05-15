using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Common;

public sealed class ActiveUserRequirement : IAuthorizationRequirement;

public sealed class ActiveUserAuthorizationHandler(TrustMapDbContext dbContext)
    : AuthorizationHandler<ActiveUserRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveUserRequirement requirement)
    {
        var subject = context.User.FindFirstValue("sub")
            ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(subject, out var userId))
        {
            return;
        }

        var status = await dbContext.Users
            .AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => (UserAccountStatus?)x.Status)
            .SingleOrDefaultAsync();

        if (status == UserAccountStatus.Active)
        {
            context.Succeed(requirement);
        }
    }
}
