using TrustMap.Domain.Common;

namespace TrustMap.Infrastructure.Persistence;

internal static class DefaultCategorySeeds
{
    internal static readonly DateTime SeedTimestampUtc = new(2026, 4, 8, 0, 0, 0, DateTimeKind.Utc);

    internal static readonly SeedCategory[] All =
    [
        new("D53A109F-9617-4A0A-B95A-5AD277A30764", "Restaurants")
    ];

    internal sealed record SeedCategory(string Id, string Name)
    {
        internal string NormalizedName => TextNormalizer.NormalizeForLookup(Name);
    }
}
