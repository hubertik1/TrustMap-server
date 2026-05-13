using TrustMap.Api.Common;
using TrustMap.Api.Features.Auth;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class UserSearchTests
{
    [Fact]
    public async Task Search_ByHandle_StillWorks()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var searchClient = await CreateAuthenticatedSearchClientAsync(factory);
        var target = await CreateUserAsync(factory, "hubert.mar", "Different Name");
        var handleBase = HandleBase(target.User.Handle);

        var baseResults = await searchClient.SearchUsersAsync(handleBase, take: 10);
        var atBaseResults = await searchClient.SearchUsersAsync($"@{handleBase}", take: 10);
        var fullHandleResults = await searchClient.SearchUsersAsync(SpacedHandleQuery(target.User.Handle), take: 10);

        Assert.Contains(baseResults, x => x.User.Id == target.User.Id);
        Assert.Contains(atBaseResults, x => x.User.Id == target.User.Id);
        Assert.Contains(fullHandleResults, x => x.User.Id == target.User.Id);
    }

    [Fact]
    public async Task Search_ByDisplayName_Works()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var searchClient = await CreateAuthenticatedSearchClientAsync(factory);
        var target = await CreateUserAsync(factory, "display-target", "Hubert Kowalski");

        var results = await searchClient.SearchUsersAsync("Hubert", take: 10);

        Assert.Contains(results, x => x.User.Id == target.User.Id);
    }

    [Fact]
    public async Task Search_ByPartialDisplayNameToken_Works()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var searchClient = await CreateAuthenticatedSearchClientAsync(factory);
        var target = await CreateUserAsync(factory, "partial-target", "Hubert Kowalski");

        var results = await searchClient.SearchUsersAsync("kowal", take: 10);

        Assert.Contains(results, x => x.User.Id == target.User.Id);
    }

    [Fact]
    public async Task Search_ByMultipleDisplayNameTokens_Works()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var searchClient = await CreateAuthenticatedSearchClientAsync(factory);
        var target = await CreateUserAsync(factory, "multi-target", "Hubert Kowalski");

        var results = await searchClient.SearchUsersAsync("hub kow", take: 10);

        Assert.Contains(results, x => x.User.Id == target.User.Id);
    }

    [Fact]
    public async Task Search_ByReversedDisplayNameTokens_Works()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var searchClient = await CreateAuthenticatedSearchClientAsync(factory);
        var target = await CreateUserAsync(factory, "reverse-target", "Hubert Kowalski");

        var results = await searchClient.SearchUsersAsync("kow hub", take: 10);

        Assert.Contains(results, x => x.User.Id == target.User.Id);
    }

    [Fact]
    public async Task Search_ByDisplayName_IsDiacriticInsensitive()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var searchClient = await CreateAuthenticatedSearchClientAsync(factory);
        var target = await CreateUserAsync(factory, "diacritic-target", "Łukasz Nowak");
        var plainTarget = await CreateUserAsync(factory, "plain-diacritic-target", "Lukasz Plain");

        var results = await searchClient.SearchUsersAsync("Lukasz", take: 10);
        var accentedQueryResults = await searchClient.SearchUsersAsync("Łukasz", take: 10);

        Assert.Contains(results, x => x.User.Id == target.User.Id);
        Assert.Contains(accentedQueryResults, x => x.User.Id == plainTarget.User.Id);
    }

    [Fact]
    public async Task Search_DoesNotDuplicateUser_WhenHandleAndDisplayNameBothMatch()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var searchClient = await CreateAuthenticatedSearchClientAsync(factory);
        var target = await CreateUserAsync(factory, "hubert", "Hubert Match");

        var results = await searchClient.SearchUsersAsync("hubert", take: 10);

        Assert.Equal(1, results.Count(x => x.User.Id == target.User.Id));
    }

    [Fact]
    public async Task Search_KeepsRelationshipStatus()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var searchClient = await CreateAuthenticatedSearchClientAsync(factory);
        var target = await CreateUserAsync(factory, "relationship-target", "Relationship Target");
        await searchClient.SendFriendRequestAsync(target.User.Id);

        var results = await searchClient.SearchUsersAsync("Relationship", take: 10);
        var result = Assert.Single(results, x => x.User.Id == target.User.Id);

        Assert.Equal(RelationshipStatus.OutgoingRequest, result.RelationshipStatus);
    }

    [Fact]
    public async Task Search_RespectsTakeLimit()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var searchClient = await CreateAuthenticatedSearchClientAsync(factory);
        await CreateUserAsync(factory, "take-target-one", "Take Match One");
        await CreateUserAsync(factory, "take-target-two", "Take Match Two");

        var results = await searchClient.SearchUsersAsync("Take Match", take: 1);

        Assert.Single(results);
    }

    [Fact]
    public async Task Search_OneCharacterQuery_ReturnsEmptyResult()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var searchClient = await CreateAuthenticatedSearchClientAsync(factory);
        var target = await CreateUserAsync(factory, "zulu-target", "Zulu Target");

        var results = await searchClient.SearchUsersAsync("z", take: 10);

        Assert.DoesNotContain(results, x => x.User.Id == target.User.Id);
        Assert.Empty(results);
    }

    private static async Task<TestApiClient> CreateAuthenticatedSearchClientAsync(TrustMapWebApplicationFactory factory)
    {
        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("viewer", "Viewer");
        client.Authenticate(session.AccessToken);
        return client;
    }

    private static async Task<AuthSessionDto> CreateUserAsync(
        TrustMapWebApplicationFactory factory,
        string subject,
        string displayName)
    {
        var client = new TestApiClient(factory.CreateClient());
        return await client.SignInAsync(subject, displayName);
    }

    private static string HandleBase(string handle) => handle.Split('#')[0];

    private static string SpacedHandleQuery(string handle)
    {
        var parts = handle.Split('#');
        return $"@ {parts[0]} # {parts[1]}";
    }
}
