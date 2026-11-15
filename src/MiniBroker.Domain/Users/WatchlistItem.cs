namespace MiniBroker.Domain.Users;

/// <summary>An instrument a user is following.</summary>
public sealed class WatchlistItem
{
    public Guid UserId { get; private set; }
    public string Symbol { get; private set; } = default!;
    public DateTimeOffset AddedAt { get; private set; }

    private WatchlistItem() { }

    public WatchlistItem(Guid userId, string symbol, DateTimeOffset addedAt)
    {
        UserId = userId;
        Symbol = symbol;
        AddedAt = addedAt;
    }
}
