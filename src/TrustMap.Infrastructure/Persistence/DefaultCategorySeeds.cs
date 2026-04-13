using TrustMap.Domain.Common;

namespace TrustMap.Infrastructure.Persistence;

internal static class DefaultCategorySeeds
{
    internal static readonly DateTime SeedTimestampUtc = new(2026, 4, 8, 0, 0, 0, DateTimeKind.Utc);

    internal static readonly SeedCategory[] All =
    [
        new("D53A109F-9617-4A0A-B95A-5AD277A30764", "Restaurant"),
        new("11111111-1111-1111-1111-111111111101", "BBQ Spots"),
        new("11111111-1111-1111-1111-111111111102", "Football Fields"),
        new("11111111-1111-1111-1111-111111111103", "Cafés"),
        new("11111111-1111-1111-1111-111111111104", "Bars"),
        new("11111111-1111-1111-1111-111111111105", "Parks"),
        new("11111111-1111-1111-1111-111111111106", "Gyms"),
        new("11111111-1111-1111-1111-111111111107", "Playgrounds"),
        new("11111111-1111-1111-1111-111111111108", "Basketball Courts"),
        new("11111111-1111-1111-1111-111111111109", "Tennis Courts"),
        new("11111111-1111-1111-1111-11111111110A", "Swimming Pools"),
        new("11111111-1111-1111-1111-11111111110B", "Picnic Spots"),
        new("11111111-1111-1111-1111-11111111110C", "Beaches"),
        new("11111111-1111-1111-1111-11111111110D", "Lakes"),
        new("11111111-1111-1111-1111-11111111110E", "Shopping Places"),
        new("11111111-1111-1111-1111-11111111110F", "Coworking Spaces"),
        new("11111111-1111-1111-1111-111111111110", "Study Spots"),
        new("11111111-1111-1111-1111-111111111111", "Dog Parks"),
        new("11111111-1111-1111-1111-111111111112", "Cinemas"),
        new("11111111-1111-1111-1111-111111111113", "Museums")
    ];

    internal sealed record SeedCategory(string Id, string Name)
    {
        internal string NormalizedName => TextNormalizer.NormalizeForLookup(Name);
    }
}
