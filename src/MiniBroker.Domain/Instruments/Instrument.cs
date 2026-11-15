using MiniBroker.Domain.Common;

namespace MiniBroker.Domain.Instruments;

public sealed class Instrument
{
    public string Symbol { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public decimal LastPrice { get; private set; }

    private Instrument() { }

    public static Instrument Create(string symbol, string name, decimal lastPrice)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new DomainException("Symbol is required.");
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Name is required.");

        var instrument = new Instrument
        {
            Symbol = symbol.Trim().ToUpperInvariant(),
            Name = name.Trim(),
        };
        instrument.UpdatePrice(lastPrice);
        return instrument;
    }

    public void UpdatePrice(decimal price)
    {
        if (price <= 0)
            throw new DomainException("Price must be greater than zero.");
        if (decimal.Round(price, 4) != price)
            throw new DomainException("Price supports at most 4 decimal places.");
        LastPrice = price;
    }

    /// <summary>Updates the price and returns the tick to store in the price history.</summary>
    public PriceTick SetPrice(decimal price, DateTimeOffset now)
    {
        UpdatePrice(price);
        return new PriceTick(Symbol, price, now);
    }
}
