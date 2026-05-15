using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;

namespace TrustMap.Api.Common;

public static class UserQueryExtensions
{
    public static IQueryable<User> ActiveOnly(this IQueryable<User> query) =>
        query.Where(x => x.Status == UserAccountStatus.Active);
}
