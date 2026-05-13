namespace TrustMap.Api.Quotas;

public static class QuotaSubjectKeys
{
    public const string UserScope = "user";

    public static string User(Guid userId) => userId.ToString("N");
}
