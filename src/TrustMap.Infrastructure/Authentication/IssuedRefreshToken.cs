using TrustMap.Domain.Entities;

namespace TrustMap.Infrastructure.Authentication;

public sealed record IssuedRefreshToken(
    string RawToken,
    RefreshToken Entity);
