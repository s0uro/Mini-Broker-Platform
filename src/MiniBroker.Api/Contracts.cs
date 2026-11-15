using MiniBroker.Domain.Accounts;
using MiniBroker.Domain.Instruments;
using MiniBroker.Domain.Orders;
using MiniBroker.Domain.Users;

namespace MiniBroker.Api;

// ---- Auth ----
public sealed record RegisterRequest(string Email, string Password, string DisplayName);
public sealed record LoginRequest(string Email, string Password);

public sealed record UserResponse(Guid Id, string Email, string DisplayName)
{
    public static UserResponse From(User u) => new(u.Id, u.Email, u.DisplayName);
}

public sealed record AuthResponse(string Token, DateTimeOffset ExpiresAt, UserResponse User);

// ---- Accounts ----
public sealed record OpenAccountRequest(string Name);
public sealed record MoneyRequest(decimal Amount);

public sealed record PositionResponse(string Symbol, decimal Quantity, decimal ReservedQuantity, decimal AveragePrice);

public sealed record AccountResponse(
    Guid Id,
    string Name,
    decimal CashBalance,
    decimal ReservedCash,
    decimal AvailableCash,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PositionResponse> Positions)
{
    public static AccountResponse From(Account a) => new(
        a.Id,
        a.Name,
        a.CashBalance,
        a.ReservedCash,
        a.AvailableCash,
        a.CreatedAt,
        a.Positions
            .OrderBy(p => p.Symbol)
            .Select(p => new PositionResponse(p.Symbol, p.Quantity, p.ReservedQuantity, p.AveragePrice))
            .ToList());
}

public sealed record TransactionResponse(
    Guid Id,
    TransactionType Type,
    decimal Amount,
    decimal BalanceAfter,
    string? Symbol,
    decimal? Quantity,
    decimal? Price,
    Guid? OrderId,
    DateTimeOffset CreatedAt)
{
    public static TransactionResponse From(CashTransaction t) =>
        new(t.Id, t.Type, t.Amount, t.BalanceAfter, t.Symbol, t.Quantity, t.Price, t.OrderId, t.CreatedAt);
}

// ---- Orders ----
public sealed record PlaceOrderRequest(
    string Symbol,
    OrderSide Side,
    decimal Quantity,
    OrderType Type = OrderType.Market,
    decimal? LimitPrice = null);

public sealed record OrderResponse(
    Guid Id,
    Guid AccountId,
    string Symbol,
    OrderType Type,
    OrderSide Side,
    decimal Quantity,
    decimal? LimitPrice,
    decimal? ExecutedPrice,
    OrderStatus Status,
    string? RejectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt)
{
    public static OrderResponse From(Order o) => new(
        o.Id, o.AccountId, o.Symbol, o.Type, o.Side, o.Quantity, o.LimitPrice, o.ExecutedPrice,
        o.Status, o.RejectionReason, o.CreatedAt, o.ClosedAt);
}

// ---- Market data ----
public sealed record UpdatePriceRequest(decimal Price);

/// <param name="ChangePercent">Change versus the oldest price in the last 24 hours, as a fraction (0.012 = +1.2%).</param>
public sealed record InstrumentResponse(string Symbol, string Name, decimal LastPrice, decimal? ChangePercent)
{
    public static InstrumentResponse From(Instrument i, decimal? referencePrice = null) => new(
        i.Symbol,
        i.Name,
        i.LastPrice,
        referencePrice is > 0 ? Math.Round(i.LastPrice / referencePrice.Value - 1, 6) : null);
}

public sealed record PricePointResponse(DateTimeOffset Timestamp, decimal Price);
