using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MiniBroker.Api;

namespace MiniBroker.Tests.Api;

public class AuthTests(BrokerApiFactory factory) : IClassFixture<BrokerApiFactory>
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = BrokerApiFactory.Json;

    [Fact]
    public async Task Register_then_login_returns_token_that_works()
    {
        var client = factory.CreateClient();
        var email = $"Alice-{Guid.NewGuid():N}@Test.dev";

        var register = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "s3cret-password", "Alice"), Json);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        // Email is case-insensitive.
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email.ToUpperInvariant(), "s3cret-password"), Json);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        Assert.Equal(email.ToLowerInvariant(), auth.User.Email);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        var me = await client.GetFromJsonAsync<UserResponse>("/api/auth/me", Json);
        Assert.Equal("Alice", me!.DisplayName);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_both_return_401()
    {
        var client = factory.CreateClient();
        var email = $"bob-{Guid.NewGuid():N}@test.dev";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "s3cret-password", "Bob"), Json);

        var wrong = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "nope-nope-nope"), Json);
        var unknown = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("ghost@test.dev", "whatever123"), Json);

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
    }

    [Fact]
    public async Task Duplicate_email_returns_409()
    {
        var client = factory.CreateClient();
        var email = $"dup-{Guid.NewGuid():N}@test.dev";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "s3cret-password", "A"), Json);

        var again = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email.ToUpperInvariant(), "s3cret-password", "B"), Json);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Theory]
    [InlineData("not-an-email", "s3cret-password")]
    [InlineData("short@test.dev", "short")]
    public async Task Invalid_registration_returns_400(string email, string password)
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password, "X"), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/accounts")]
    [InlineData("/api/instruments")]
    [InlineData("/api/watchlist")]
    [InlineData("/api/auth/me")]
    public async Task Protected_endpoints_require_a_token(string url)
    {
        var response = await factory.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Tampered_token_is_rejected()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.bad");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/accounts")).StatusCode);
    }

    [Fact]
    public async Task Users_cannot_see_or_use_each_others_accounts()
    {
        var alice = await factory.CreateAuthenticatedClientAsync();
        var mallory = await factory.CreateAuthenticatedClientAsync();

        var created = await alice.PostAsJsonAsync("/api/accounts", new OpenAccountRequest("Alice main"), Json);
        var account = (await created.Content.ReadFromJsonAsync<AccountResponse>(Json))!;

        Assert.Equal(HttpStatusCode.NotFound, (await mallory.GetAsync($"/api/accounts/{account.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await mallory.PostAsJsonAsync($"/api/accounts/{account.Id}/withdrawals", new MoneyRequest(1m), Json)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mallory.GetAsync($"/api/accounts/{account.Id}/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mallory.GetAsync($"/api/accounts/{account.Id}/transactions")).StatusCode);
        Assert.Empty((await mallory.GetFromJsonAsync<List<AccountResponse>>("/api/accounts", Json))!);
        Assert.Single((await alice.GetFromJsonAsync<List<AccountResponse>>("/api/accounts", Json))!);
    }
}
