using System.Net.Mail;
using MiniBroker.Domain.Common;

namespace MiniBroker.Domain.Users;

public sealed class User
{
    public Guid Id { get; private set; }

    /// <summary>Stored lower-cased so lookups are case-insensitive.</summary>
    public string Email { get; private set; } = default!;

    public string DisplayName { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }

    private User() { }

    public static User Register(string email, string displayName, DateTimeOffset now)
    {
        var normalized = NormalizeEmail(email);
        if (!MailAddress.TryCreate(normalized, out var parsed) || parsed.Address != normalized || normalized.Length > 256)
            throw new DomainException("A valid email address is required.");
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 100)
            throw new DomainException("Display name is required (max 100 characters).");

        return new User
        {
            Id = Guid.NewGuid(),
            Email = normalized,
            DisplayName = displayName.Trim(),
            CreatedAt = now,
        };
    }

    public static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public void SetPasswordHash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            throw new ArgumentException("Hash is required.", nameof(hash));
        PasswordHash = hash;
    }
}
