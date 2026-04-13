using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TrustMap.Domain.Common;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Services;

public sealed class HandleGenerator(TrustMapDbContext dbContext)
{
    private static readonly Regex HandleSuffixRegex = new(@"^(?<base>[a-z0-9._-]+)#(?<suffix>\d{4})$", RegexOptions.Compiled);
    private const int MaximumHandleLength = 32;
    private const int SuffixLength = 5; // "#1234"
    private const int MaximumBaseLength = MaximumHandleLength - SuffixLength;

    public async Task<string> GenerateInitialAppleHandleAsync(
        string? email,
        string? displayName,
        string? fallbackHandle,
        CancellationToken cancellationToken)
    {
        var emailPrefix = email?.Split('@', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        var requestedBase = ExtractEditableBase(fallbackHandle);
        var baseHandle = NormalizeBaseHandle(
            emailPrefix ?? requestedBase ?? displayName,
            "user");

        return await GenerateAvailableHandleAsync(
            baseHandle,
            currentUserId: null,
            preferredSuffix: null,
            cancellationToken);
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
        var baseHandle = NormalizeBaseHandle(
            requestedBase ?? displayName ?? emailPrefix,
            "user");
        var preservedSuffix = ExtractSuffix(currentHandle);

        return await GenerateAvailableHandleAsync(
            baseHandle,
            currentUserId,
            preservedSuffix,
            cancellationToken);
    }

    public async Task<string> GenerateUniqueHandleAsync(
        string? preferredHandle,
        string? displayName,
        string? email,
        Guid? currentUserId,
        CancellationToken cancellationToken)
    {
        var emailPrefix = email?.Split('@', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        var requestedBase = ExtractEditableBase(preferredHandle);
        var baseHandle = NormalizeBaseHandle(
            requestedBase ?? displayName ?? emailPrefix,
            "user");

        return await GenerateAvailableHandleAsync(
            baseHandle,
            currentUserId,
            preferredSuffix: null,
            cancellationToken);
    }

    private async Task<string> GenerateAvailableHandleAsync(
        string baseHandle,
        Guid? currentUserId,
        string? preferredSuffix,
        CancellationToken cancellationToken)
    {
        if (preferredSuffix is not null)
        {
            var preservedCandidate = $"{baseHandle}#{preferredSuffix}";
            var preservedExists = await HandleExistsAsync(preservedCandidate, currentUserId, cancellationToken);

            if (!preservedExists)
            {
                return preservedCandidate;
            }
        }

        while (true)
        {
            var candidate = $"{baseHandle}#{CreateRandomSuffix()}";
            var exists = await HandleExistsAsync(candidate, currentUserId, cancellationToken);

            if (!exists)
            {
                return candidate;
            }
        }
    }

    private async Task<bool> HandleExistsAsync(
        string candidate,
        Guid? currentUserId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Users.AnyAsync(
            x => x.NormalizedHandle == candidate && (!currentUserId.HasValue || x.Id != currentUserId.Value),
            cancellationToken);
    }

    private static string NormalizeBaseHandle(string? value, string fallback)
    {
        var baseHandle = TextNormalizer.SlugifyOrFallback(value, fallback);

        if (baseHandle.Length < 3)
        {
            baseHandle = $"{baseHandle}user";
        }

        return baseHandle[..Math.Min(baseHandle.Length, MaximumBaseLength)];
    }

    private static string CreateRandomSuffix() =>
        RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4");

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
