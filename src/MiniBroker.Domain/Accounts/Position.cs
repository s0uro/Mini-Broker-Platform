namespace MiniBroker.Domain.Accounts;

/// <summary>
/// Holdings of one instrument inside an account. Part of the quantity can be reserved by pending sell orders.
/// </summary>
public sealed class Position
{
    public Guid AccountId { get; private set; }
    public string Symbol { get; private set; } = default!;
    public decimal Quantity { get; private set; }
    public decimal ReservedQuantity { get; private set; }
    public decimal AveragePrice { get; private set; }

    public decimal AvailableQuantity => Quantity - ReservedQuantity;

    private Position() { }

    internal Position(Guid accountId, string symbol)
    {
        AccountId = accountId;
        Symbol = symbol;
    }

    internal void Add(decimal quantity, decimal price)
    {
        var newQuantity = Quantity + quantity;
        AveragePrice = Math.Round((Quantity * AveragePrice + quantity * price) / newQuantity, Money.Scale);
        Quantity = newQuantity;
    }

    internal void Remove(decimal quantity) => Quantity -= quantity;

    internal void Reserve(decimal quantity) => ReservedQuantity += quantity;

    internal void Release(decimal quantity) => ReservedQuantity -= quantity;
}
