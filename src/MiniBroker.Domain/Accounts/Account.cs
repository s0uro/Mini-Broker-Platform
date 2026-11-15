using MiniBroker.Domain.Common;
using MiniBroker.Domain.Instruments;
using MiniBroker.Domain.Orders;

namespace MiniBroker.Domain.Accounts;

/// <summary>
/// A trading account owned by a user: cash balance, positions, and reservations held by pending
/// limit orders. Every change to cash and holdings goes through here and is written to the ledger.
/// </summary>
public sealed class Account
{
    private readonly List<Position> _positions = [];
    private readonly List<CashTransaction> _newTransactions = [];

    public Guid Id { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public string Name { get; private set; } = default!;
    public decimal CashBalance { get; private set; }

    /// <summary>Cash held by pending limit buy orders.</summary>
    public decimal ReservedCash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public decimal AvailableCash => CashBalance - ReservedCash;
    public IReadOnlyCollection<Position> Positions => _positions.AsReadOnly();

    private Account() { }

    public static Account Open(Guid ownerUserId, string name, DateTimeOffset now)
    {
        if (ownerUserId == Guid.Empty)
            throw new DomainException("Owner is required.");
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Account name is required.");
        if (name.Trim().Length > 100)
            throw new DomainException("Account name must be at most 100 characters.");

        return new Account
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Name = name.Trim(),
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Ledger entries created since the last call. The persistence layer saves them with the account.
    /// </summary>
    public IReadOnlyList<CashTransaction> DequeueNewTransactions()
    {
        var items = _newTransactions.ToList();
        _newTransactions.Clear();
        return items;
    }

    public void Deposit(decimal amount, DateTimeOffset now)
    {
        EnsureValidMoney(amount);
        CashBalance += amount;
        _newTransactions.Add(CashTransaction.Cash(Id, TransactionType.Deposit, amount, CashBalance, now));
    }

    public void Withdraw(decimal amount, DateTimeOffset now)
    {
        EnsureValidMoney(amount);
        if (amount > AvailableCash)
            throw new DomainException("Insufficient available funds.");
        CashBalance -= amount;
        _newTransactions.Add(CashTransaction.Cash(Id, TransactionType.Withdrawal, -amount, CashBalance, now));
    }

    /// <summary>
    /// Places an order. Market orders execute now or are rejected. Limit orders execute now if the
    /// market price already satisfies the limit; otherwise they reserve cash/holdings and stay Pending.
    /// Business-rule failures produce a rejected order rather than an exception.
    /// </summary>
    public Order PlaceOrder(
        Instrument instrument, OrderType type, OrderSide side, decimal quantity, decimal? limitPrice, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        EnsureValidQuantity(quantity);
        if (!Enum.IsDefined(side))
            throw new DomainException($"Unknown order side '{side}'.");

        switch (type)
        {
            case OrderType.Market:
                limitPrice = null;
                break;
            case OrderType.Limit:
                if (limitPrice is not { } limit || limit <= 0)
                    throw new DomainException("Limit price must be greater than zero.");
                if (decimal.Round(limit, Money.Scale) != limit)
                    throw new DomainException($"Limit price supports at most {Money.Scale} decimal places.");
                break;
            default:
                throw new DomainException($"Unknown order type '{type}'.");
        }

        var order = Order.Create(Id, instrument.Symbol, type, side, quantity, limitPrice, now);
        var price = instrument.LastPrice;

        if (order.IsMarketableAt(price))
        {
            var failure = TryExecute(order, price, now);
            if (failure is not null)
                order.MarkRejected(failure, now);
            return order;
        }

        // Resting limit order: reserve what it will need so it can't be spent twice.
        var reserveFailure = side == OrderSide.Buy ? TryReserveCash(order) : TryReserveHoldings(order);
        if (reserveFailure is not null)
            order.MarkRejected(reserveFailure, now);
        return order;
    }

    /// <summary>
    /// Executes a pending limit order if <paramref name="marketPrice"/> satisfies its limit.
    /// Returns true when the order was filled (or rejected) and should be saved.
    /// </summary>
    public bool TryFillPendingOrder(Order order, decimal marketPrice, DateTimeOffset now)
    {
        EnsureOwnPendingOrder(order);
        if (!order.IsMarketableAt(marketPrice))
            return false;

        ReleaseReservation(order);
        var failure = TryExecute(order, marketPrice, now);
        if (failure is not null)
            order.MarkRejected(failure, now);
        return true;
    }

    public void CancelOrder(Order order, DateTimeOffset now)
    {
        EnsureOwnPendingOrder(order);
        ReleaseReservation(order);
        order.MarkCancelled(now);
    }

    // Returns a rejection reason, or null when the order was filled.
    private string? TryExecute(Order order, decimal price, DateTimeOffset now)
    {
        var amount = Math.Round(order.Quantity * price, Money.Scale);

        if (order.Side == OrderSide.Buy)
        {
            if (amount > AvailableCash)
                return "Insufficient funds.";

            CashBalance -= amount;
            var position = FindPosition(order.Symbol);
            if (position is null)
            {
                position = new Position(Id, order.Symbol);
                _positions.Add(position);
            }
            position.Add(order.Quantity, price);
            _newTransactions.Add(CashTransaction.Trade(
                Id, TransactionType.Buy, -amount, CashBalance, order.Symbol, order.Quantity, price, order.Id, now));
        }
        else
        {
            var position = FindPosition(order.Symbol);
            if (position is null || position.AvailableQuantity < order.Quantity)
                return "Insufficient holdings.";

            CashBalance += amount;
            position.Remove(order.Quantity);
            if (position.Quantity == 0)
                _positions.Remove(position);
            _newTransactions.Add(CashTransaction.Trade(
                Id, TransactionType.Sell, amount, CashBalance, order.Symbol, order.Quantity, price, order.Id, now));
        }

        order.MarkFilled(price, now);
        return null;
    }

    private string? TryReserveCash(Order order)
    {
        var needed = ReservedAmount(order);
        if (needed > AvailableCash)
            return "Insufficient funds.";
        ReservedCash += needed;
        return null;
    }

    private string? TryReserveHoldings(Order order)
    {
        var position = FindPosition(order.Symbol);
        if (position is null || position.AvailableQuantity < order.Quantity)
            return "Insufficient holdings.";
        position.Reserve(order.Quantity);
        return null;
    }

    private void ReleaseReservation(Order order)
    {
        if (order.Side == OrderSide.Buy)
            ReservedCash -= ReservedAmount(order);
        else
            FindPosition(order.Symbol)?.Release(order.Quantity);
    }

    private static decimal ReservedAmount(Order order) => Math.Round(order.Quantity * order.LimitPrice!.Value, Money.Scale);

    private void EnsureOwnPendingOrder(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.AccountId != Id)
            throw new DomainException("The order does not belong to this account.");
        if (!order.IsPending)
            throw new DomainException($"Only pending orders can be changed (this one is {order.Status}).");
    }

    private Position? FindPosition(string symbol) => _positions.Find(p => p.Symbol == symbol);

    private static void EnsureValidQuantity(decimal quantity)
    {
        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");
        if (decimal.Round(quantity, Money.QuantityScale) != quantity)
            throw new DomainException($"Quantity supports at most {Money.QuantityScale} decimal places.");
    }

    private static void EnsureValidMoney(decimal amount)
    {
        if (amount <= 0)
            throw new DomainException("Amount must be greater than zero.");
        if (decimal.Round(amount, Money.Scale) != amount)
            throw new DomainException($"Amount supports at most {Money.Scale} decimal places.");
    }
}
