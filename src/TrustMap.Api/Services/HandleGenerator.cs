using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TrustMap.Domain.Common;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Services;

public sealed class HandleGenerator(TrustMapDbContext dbContext)
{
    private static readonly Regex HandleSuffixRegex = new(@"^(?<base>[a-z0-9._-]+)#(?<suffix>\d{4})$", RegexOptions.Compiled);

    public async Task<string> GenerateInitialAppleHandleAsync(
        string? email,
        string? displayName,
        string? fallbackHandle,
        CancellationToken cancellationToken)
    {
        var emailPrefix = email?.Split('@', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        var baseHandle = TextNormalizer.SlugifyOrFallback(
            emailPrefix ?? fallbackHandle ?? displayName,
            "user");

        if (baseHandle.Length < 3)
        {
            baseHandle = $"{baseHandle}user";
        }

        baseHandle = baseHandle[..Math.Min(baseHandle.Length, 19)];

        while (true)
        {
            var suffix = RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4");
            var candidate = $"{baseHandle}#{suffix}";

            var exists = await dbContext.Users.AnyAsync(
                x => x.NormalizedHandle == candidate,
                cancellationToken);

            if (!exists)
            {
                return candidate;
            }
        }
    }

    public async Task<string> GenerateUpdatedHandleAsync(
        string? preferredBaseHandle,
        string currentHandle,
        string? displayName,
        string? email,
        Guid currentUserId,
        CancellationToken cancellationToken)
    {
        var emailPrefix = email?.Split('@', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        var requestedBase = ExtractEditableBase(preferredBaseHandle);
        var baseHandle = TextNormalizer.SlugifyOrFallback(
            requestedBase ?? displayName ?? emailPrefix,
            "user");

        if (baseHandle.Length < 3)
        {
            baseHandle = $"{baseHandle}user";
        }

        baseHandle = baseHandle[..Math.Min(baseHandle.Length, 27)];
        var preservedSuffix = ExtractSuffix(currentHandle);

        if (preservedSuffix is not null)
        {
            var preservedCandidate = $"{baseHandle}#{preservedSuffix}";
            var preservedExists = await dbContext.Users.AnyAsync(
                x => x.NormalizedHandle == preservedCandidate && x.Id != currentUserId,
                cancellationToken);

            if (!preservedExists)
            {
                return preservedCandidate;
            }
        }

        while (true)
        {
            var suffix = RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4");
            var candidate = $"{baseHandle}#{suffix}";

            var exists = await dbContext.Users.AnyAsync(
                x => x.NormalizedHandle == candidate && x.Id != currentUserId,
                cancellationToken);

            if (!exists)
            {
                return candidate;
            }
        }
    }

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

    private static string? ExtractSuffix(string handle)
    {
        var match = HandleSuffixRegex.Match(TextNormalizer.NormalizeHandle(handle));
        return match.Success ? match.Groups["suffix"].Value : null;
    }

    private static string? ExtractEditableBase(string? handle)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            return null;
        }

        var normalized = handle.Trim();
        var match = HandleSuffixRegex.Match(TextNormalizer.NormalizeHandle(normalized));
        if (match.Success)
        {
            return match.Groups["base"].Value;
        }

        var separatorIndex = normalized.IndexOf('#');
        if (separatorIndex >= 0)
        {
            normalized = normalized[..separatorIndex];
        }

        return normalized;
    }
}
