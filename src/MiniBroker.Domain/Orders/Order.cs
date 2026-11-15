namespace MiniBroker.Domain.Orders;

public enum OrderSide
{
    Buy,
    Sell,
}

public enum OrderType
{
    /// <summary>Executes immediately at the instrument's last price.</summary>
    Market,

    /// <summary>Executes only at the limit price or better; waits as Pending until then.</summary>
    Limit,
}

public enum OrderStatus
{
    Pending,
    Filled,
    Rejected,
    Cancelled,
}

/// <summary>
/// An order on one instrument. State changes go through <see cref="Accounts.Account"/>, which keeps
/// cash, holdings and reservations consistent with the order's status.
/// </summary>
public sealed class Order
{
    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public string Symbol { get; private set; } = default!;
    public OrderType Type { get; private set; }
    public OrderSide Side { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal? LimitPrice { get; private set; }
    public decimal? ExecutedPrice { get; private set; }
    public OrderStatus Status { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsPending => Status == OrderStatus.Pending;

    private Order() { }

    internal static Order Create(
        Guid accountId, string symbol, OrderType type, OrderSide side, decimal quantity, decimal? limitPrice, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Symbol = symbol,
            Type = type,
            Side = side,
            Quantity = quantity,
            LimitPrice = limitPrice,
            Status = OrderStatus.Pending,
            CreatedAt = now,
        };

    /// <summary>True when a limit order would execute at <paramref name="marketPrice"/>.</summary>
    public bool IsMarketableAt(decimal marketPrice) => Type switch
    {
        OrderType.Market => true,
        OrderType.Limit when Side == OrderSide.Buy => marketPrice <= LimitPrice,
        OrderType.Limit => marketPrice >= LimitPrice,
        _ => false,
    };

    internal void MarkFilled(decimal price, DateTimeOffset now)
    {
        ExecutedPrice = price;
        Status = OrderStatus.Filled;
        ClosedAt = now;
    }

    internal void MarkRejected(string reason, DateTimeOffset now)
    {
        RejectionReason = reason;
        Status = OrderStatus.Rejected;
        ClosedAt = now;
    }

    internal void MarkCancelled(DateTimeOffset now)
    {
        Status = OrderStatus.Cancelled;
        ClosedAt = now;
    }
}
