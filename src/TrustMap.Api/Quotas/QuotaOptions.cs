using Microsoft.Extensions.Options;

namespace TrustMap.Api.Quotas;

public sealed class QuotaOptions
{
    public const string SectionName = "Quotas";

    public bool Enabled { get; init; } = true;

    public UploadQuotaOptions Uploads { get; init; } = new();

    public SearchQuotaOptions Search { get; init; } = new();

    public FriendRequestQuotaOptions FriendRequests { get; init; } = new();
}

public sealed class UploadQuotaOptions
{
    public int MaxPhotosPerUserPerDay { get; init; } = 100;

    public long MaxBytesPerUserPerDay { get; init; } = 104_857_600;

    public int MaxAvatarChangesPerUserPerDay { get; init; } = 20;
}

public sealed class SearchQuotaOptions
{
    public int MaxSearchesPerUserPerDay { get; init; } = 5_000;

    public int MinimumQueryLength { get; init; } = 2;
}

public sealed class FriendRequestQuotaOptions
{
    public int MaxOutgoingPerUserPerHour { get; init; } = 10;

    public int MaxOutgoingPerUserPerDay { get; init; } = 50;

    public int MaxPendingOutgoingPerUser { get; init; } = 100;

    public int RejectedTargetCooldownHours { get; init; } = 24;
}

public sealed class QuotaOptionsValidator : IValidateOptions<QuotaOptions>
{
    public ValidateOptionsResult Validate(string? name, QuotaOptions options)
    {
        var failures = new List<string>();

        if (options.Uploads.MaxPhotosPerUserPerDay <= 0)
        {
            failures.Add("Quotas:Uploads:MaxPhotosPerUserPerDay must be positive.");
        }

        if (options.Uploads.MaxBytesPerUserPerDay <= 0)
        {
            failures.Add("Quotas:Uploads:MaxBytesPerUserPerDay must be positive.");
        }

        if (options.Uploads.MaxAvatarChangesPerUserPerDay <= 0)
        {
            failures.Add("Quotas:Uploads:MaxAvatarChangesPerUserPerDay must be positive.");
        }

        if (options.Search.MaxSearchesPerUserPerDay < 0)
        {
            failures.Add("Quotas:Search:MaxSearchesPerUserPerDay cannot be negative.");
        }

        if (options.Search.MinimumQueryLength <= 0)
        {
            failures.Add("Quotas:Search:MinimumQueryLength must be positive.");
        }

        if (options.FriendRequests.MaxOutgoingPerUserPerHour <= 0)
        {
            failures.Add("Quotas:FriendRequests:MaxOutgoingPerUserPerHour must be positive.");
        }

        if (options.FriendRequests.MaxOutgoingPerUserPerDay <= 0)
        {
            failures.Add("Quotas:FriendRequests:MaxOutgoingPerUserPerDay must be positive.");
        }

        if (options.FriendRequests.MaxPendingOutgoingPerUser <= 0)
        {
            failures.Add("Quotas:FriendRequests:MaxPendingOutgoingPerUser must be positive.");
        }

        if (options.FriendRequests.RejectedTargetCooldownHours < 0)
        {
            failures.Add("Quotas:FriendRequests:RejectedTargetCooldownHours cannot be negative.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
