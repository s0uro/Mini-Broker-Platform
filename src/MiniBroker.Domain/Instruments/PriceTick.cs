namespace MiniBroker.Domain.Instruments;

/// <summary>A historical price point for an instrument.</summary>
public sealed class PriceTick
{
    public long Id { get; private set; }
    public string Symbol { get; private set; } = default!;
    public decimal Price { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }

    private PriceTick() { }

    public PriceTick(string symbol, decimal price, DateTimeOffset timestamp)
    {
        Symbol = symbol;
        Price = price;
        Timestamp = timestamp;
    }
}
