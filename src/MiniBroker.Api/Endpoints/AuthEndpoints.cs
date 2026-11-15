using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MiniBroker.Api.Auth;
using MiniBroker.Domain.Common;
using MiniBroker.Domain.Users;
using MiniBroker.Infrastructure.Persistence;

namespace MiniBroker.Api.Endpoints;

public static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";
    private const int MinPasswordLength = 8;
    private const int MaxPasswordLength = 128;

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", Register).AllowAnonymous().RequireRateLimiting(RateLimitPolicy);
        group.MapPost("/login", Login).AllowAnonymous().RequireRateLimiting(RateLimitPolicy);
        group.MapGet("/me", Me).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Created<AuthResponse>, Conflict<ProblemDetailsLite>>> Register(
        RegisterRequest request, BrokerDbContext db, IPasswordHasher<User> hasher, TokenService tokens,
        TimeProvider clock, CancellationToken ct)
    {
        var password = request.Password ?? string.Empty;
        if (password.Length is < MinPasswordLength or > MaxPasswordLength)
            throw new DomainException($"Password must be {MinPasswordLength}-{MaxPasswordLength} characters.");

        var user = User.Register(request.Email, request.DisplayName, clock.GetUtcNow());
        if (await db.Users.AnyAsync(u => u.Email == user.Email, ct))
            return TypedResults.Conflict(new ProblemDetailsLite("An account with this email already exists."));

        user.SetPasswordHash(hasher.HashPassword(user, password));
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race with a concurrent registration for the same email (unique index).
            return TypedResults.Conflict(new ProblemDetailsLite("An account with this email already exists."));
        }

        return TypedResults.Created("/api/auth/me", tokens.Issue(user));
    }

    private static async Task<Results<Ok<AuthResponse>, UnauthorizedHttpResult>> Login(
        LoginRequest request, BrokerDbContext db, IPasswordHasher<User> hasher, TokenService tokens, CancellationToken ct)
    {
        var email = User.NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);

        if (user is null)
        {
            // Hash anyway so response time doesn't reveal whether the email exists.
            hasher.HashPassword(DummyUser, request.Password ?? string.Empty);
            return TypedResults.Unauthorized();
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? string.Empty);
        if (result == PasswordVerificationResult.Failed)
            return TypedResults.Unauthorized();

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.SetPasswordHash(hasher.HashPassword(user, request.Password!));
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(tokens.Issue(user));
    }

    private static async Task<Results<Ok<UserResponse>, UnauthorizedHttpResult>> Me(
        HttpContext http, BrokerDbContext db, CancellationToken ct)
    {
        var userId = http.User.GetUserId();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? TypedResults.Unauthorized() : TypedResults.Ok(UserResponse.From(user));
    }

    private static readonly User DummyUser = User.Register("nobody@example.invalid", "nobody", DateTimeOffset.UnixEpoch);
}

/// <summary>Minimal error body for typed results that carry a message.</summary>
public sealed record ProblemDetailsLite(string Detail);
