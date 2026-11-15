using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MiniBroker.Api;
using MiniBroker.Infrastructure.Persistence;

namespace MiniBroker.Tests.Api;

/// <summary>
/// Runs the real API in memory, with the SQL Server DbContext swapped for an isolated in-memory database.
/// </summary>
public sealed class BrokerApiFactory : WebApplicationFactory<Program>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _databaseName = $"broker-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:BrokerDb", "Server=unused");
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-that-is-long-enough-1234567890");
        builder.UseSetting("RateLimiting:AuthPermitsPerMinute", "1000");
        builder.UseSetting("Market:SimulatorEnabled", "false");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<BrokerDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<BrokerDbContext>>();
            services.AddDbContext<BrokerDbContext>(o => o.UseInMemoryDatabase(_databaseName));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<BrokerDbContext>().Database.EnsureCreated(); // applies seed data
        return host;
    }

    /// <summary>Registers a fresh user and returns a client that sends their bearer token.</summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync(string? email = null)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email ?? $"user-{Guid.NewGuid():N}@test.dev", "correct-horse-battery", "Test User"), Json);
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return client;
    }
}
