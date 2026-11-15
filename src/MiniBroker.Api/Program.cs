using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MiniBroker.Api;
using MiniBroker.Api.Auth;
using MiniBroker.Api.Endpoints;
using MiniBroker.Api.Market;
using MiniBroker.Domain.Users;
using MiniBroker.Infrastructure;
using MiniBroker.Infrastructure.Persistence;

const string DevKeyPrefix = "DEV-ONLY";

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
var isDevelopment = builder.Environment.IsDevelopment();

builder.Services.AddInfrastructure(config);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<KnownExceptionHandler>();
builder.Services.AddHealthChecks().AddDbContextCheck<BrokerDbContext>();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ---- Auth (JWT bearer) ----
builder.Services.AddOptions<JwtOptions>()
    .Bind(config.GetSection(JwtOptions.SectionName))
    .Validate(o => Encoding.UTF8.GetByteCount(o.SigningKey) >= 32, "Jwt:SigningKey must be at least 32 bytes.")
    .Validate(o => isDevelopment || !o.SigningKey.StartsWith(DevKeyPrefix, StringComparison.Ordinal),
        "The development JWT key must not be used outside Development. Set Jwt__SigningKey.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience), "Jwt:Issuer and Jwt:Audience are required.")
    .ValidateOnStart();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
    {
        bearer.MapInboundClaims = false; // keep "sub", "email", "name" as-is
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Value.Issuer,
            ValidAudience = jwt.Value.Audience,
            IssuerSigningKey = jwt.Value.CreateKey(),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Name,
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<TokenService>();

// Throttle login/register per client IP to slow down password guessing.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = config.GetValue("RateLimiting:AuthPermitsPerMinute", 10),
            Window = TimeSpan.FromMinutes(1),
        }));
});

// CORS only when the frontend is served from another origin (e.g. separate Azure Static Web App).
var allowedOrigins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
}

// ---- Market ----
builder.Services.Configure<MarketOptions>(config.GetSection(MarketOptions.SectionName));
builder.Services.AddScoped<PriceService>();
builder.Services.AddHostedService<MarketSimulator>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
if (allowedOrigins.Length > 0)
    app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (isDevelopment)
{
    app.MapOpenApi();
}

if (config.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<BrokerDbContext>().Database.MigrateAsync();
}

app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapOrderEndpoints();
app.MapInstrumentEndpoints();
app.MapWatchlistEndpoints();

app.Run();

public partial class Program;
