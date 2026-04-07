using Microsoft.EntityFrameworkCore;
using TrustMap.Domain.Common;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Services;

public sealed class HandleGenerator(TrustMapDbContext dbContext)
{
    public async Task<string> GenerateUniqueHandleAsync(
        string? preferredHandle,
        string? displayName,
        string? email,
        Guid? currentUserId,
        CancellationToken cancellationToken)
    {
        var emailPrefix = email?.Split('@', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        var baseHandle = TextNormalizer.SlugifyOrFallback(
            preferredHandle ?? displayName ?? emailPrefix,
            "user");

        if (baseHandle.Length < 3)
        {
            baseHandle = $"{baseHandle}user";
        }

        baseHandle = baseHandle[..Math.Min(baseHandle.Length, 24)];
        var candidate = baseHandle;
        var suffix = 0;

        while (await dbContext.Users.AnyAsync(
                   x => x.NormalizedHandle == candidate && (!currentUserId.HasValue || x.Id != currentUserId.Value),
                   cancellationToken))
        {
            suffix++;
            var suffixText = suffix.ToString();
            var prefixLength = Math.Max(3, 24 - suffixText.Length - 1);
            var prefix = baseHandle[..Math.Min(baseHandle.Length, prefixLength)];
            candidate = $"{prefix}-{suffixText}";
        }

        return candidate;
    }
}
