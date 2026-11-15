namespace MiniBroker.Domain.Accounts;

public static class Money
{
    /// <summary>Decimal places kept for money and prices (matches the DB column precision).</summary>
    public const int Scale = 4;

    /// <summary>Decimal places kept for quantities (fractional units allowed).</summary>
    public const int QuantityScale = 6;
}
