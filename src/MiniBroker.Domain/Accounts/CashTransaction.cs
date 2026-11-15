namespace MiniBroker.Domain.Accounts;

public enum TransactionType
{
    Deposit,
    Withdrawal,
    Buy,
    Sell,
}

/// <summary>
/// One entry in an account's cash ledger. Amount is signed: positive adds cash, negative removes it.
/// </summary>
public sealed class CashTransaction
{
    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public TransactionType Type { get; private set; }
    public decimal Amount { get; private set; }
    public decimal BalanceAfter { get; private set; }
    public string? Symbol { get; private set; }
    public decimal? Quantity { get; private set; }
    public decimal? Price { get; private set; }
    public Guid? OrderId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private CashTransaction() { }

    internal static CashTransaction Cash(Guid accountId, TransactionType type, decimal amount, decimal balanceAfter, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = type,
            Amount = amount,
            BalanceAfter = balanceAfter,
            CreatedAt = now,
        };

    internal static CashTransaction Trade(
        Guid accountId, TransactionType type, decimal amount, decimal balanceAfter,
        string symbol, decimal quantity, decimal price, Guid orderId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Type = type,
            Amount = amount,
            BalanceAfter = balanceAfter,
            Symbol = symbol,
            Quantity = quantity,
            Price = price,
            OrderId = orderId,
            CreatedAt = now,
        };
}
