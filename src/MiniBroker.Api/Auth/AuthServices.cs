using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MiniBroker.Domain.Users;

namespace MiniBroker.Api.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "MiniBroker";
    public string Audience { get; set; } = "MiniBroker";

    /// <summary>HMAC-SHA256 key, at least 32 bytes. Never commit a production key — use env vars / Key Vault.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 480;

    public SymmetricSecurityKey CreateKey() => new(Encoding.UTF8.GetBytes(SigningKey));
}

public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    public AuthResponse Issue(User user)
    {
        var o = options.Value;
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(o.ExpiryMinutes);

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Name, user.DisplayName),
            ]),
            SigningCredentials = new SigningCredentials(o.CreateKey(), SecurityAlgorithms.HmacSha256),
        });

        return new AuthResponse(token, expires, UserResponse.From(user));
    }
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>The authenticated user's id (the JWT "sub" claim). Only call on endpoints that require auth.</summary>
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
            ? id
            : throw new InvalidOperationException("Authenticated user has no valid 'sub' claim.");
}
