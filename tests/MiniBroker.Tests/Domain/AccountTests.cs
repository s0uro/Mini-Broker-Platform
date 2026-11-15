using MiniBroker.Domain.Accounts;
using MiniBroker.Domain.Common;
using MiniBroker.Domain.Instruments;
using MiniBroker.Domain.Orders;

namespace MiniBroker.Tests.Domain;

public class AccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Owner = Guid.NewGuid();

    private static Account FundedAccount(decimal cash)
    {
        var account = Account.Open(Owner, "Main", Now);
        account.Deposit(cash, Now);
        account.DequeueNewTransactions();
        return account;
    }

    private static Order Market(Account a, Instrument i, OrderSide side, decimal qty) =>
        a.PlaceOrder(i, OrderType.Market, side, qty, null, Now);

    private static Order Limit(Account a, Instrument i, OrderSide side, decimal qty, decimal limit) =>
        a.PlaceOrder(i, OrderType.Limit, side, qty, limit, Now);

    // ---- Market orders ----

    [Fact]
    public void Market_buy_with_enough_cash_fills_and_creates_position()
    {
        var account = FundedAccount(1_000m);
        var aapl = Instrument.Create("AAPL", "Apple", 100m);

        var order = Market(account, aapl, OrderSide.Buy, 3m);

        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(100m, order.ExecutedPrice);
        Assert.Equal(700m, account.CashBalance);
        var position = Assert.Single(account.Positions);
        Assert.Equal(("AAPL", 3m, 100m), (position.Symbol, position.Quantity, position.AveragePrice));
    }

    [Fact]
    public void Market_buy_without_enough_cash_is_rejected_and_changes_nothing()
    {
        var account = FundedAccount(100m);
        var aapl = Instrument.Create("AAPL", "Apple", 100m);

        var order = Market(account, aapl, OrderSide.Buy, 2m);

        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal("Insufficient funds.", order.RejectionReason);
        Assert.Equal(100m, account.CashBalance);
        Assert.Empty(account.Positions);
        Assert.Empty(account.DequeueNewTransactions());
    }

    [Fact]
    public void Buying_twice_updates_weighted_average_price()
    {
        var account = FundedAccount(10_000m);
        var aapl = Instrument.Create("AAPL", "Apple", 100m);

        Market(account, aapl, OrderSide.Buy, 1m);
        aapl.UpdatePrice(200m);
        Market(account, aapl, OrderSide.Buy, 3m);

        var position = Assert.Single(account.Positions);
        Assert.Equal(4m, position.Quantity);
        Assert.Equal(175m, position.AveragePrice); // (1*100 + 3*200) / 4
    }

    [Fact]
    public void Selling_entire_position_credits_cash_and_removes_position()
    {
        var account = FundedAccount(1_000m);
        var aapl = Instrument.Create("AAPL", "Apple", 100m);
        Market(account, aapl, OrderSide.Buy, 5m);
        aapl.UpdatePrice(120m);

        var order = Market(account, aapl, OrderSide.Sell, 5m);

        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(500m + 600m, account.CashBalance);
        Assert.Empty(account.Positions);
    }

    [Fact]
    public void Selling_more_than_held_is_rejected()
    {
        var account = FundedAccount(1_000m);
        var aapl = Instrument.Create("AAPL", "Apple", 100m);
        Market(account, aapl, OrderSide.Buy, 2m);

        var order = Market(account, aapl, OrderSide.Sell, 3m);

        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal("Insufficient holdings.", order.RejectionReason);
    }

    // ---- Limit orders ----

    [Fact]
    public void Limit_buy_below_market_rests_and_reserves_cash()
    {
        var account = FundedAccount(1_000m);
        var aapl = Instrument.Create("AAPL", "Apple", 100m);

        var order = Limit(account, aapl, OrderSide.Buy, 5m, 90m);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(1_000m, account.CashBalance);
        Assert.Equal(450m, account.ReservedCash);
        Assert.Equal(550m, account.AvailableCash);
        Assert.Empty(account.Positions);
    }

    [Fact]
    public void Reserved_cash_cannot_be_withdrawn_or_spent_twice()
    {
        var account = FundedAccount(1_000m);
        var aapl = Instrument.Create("AAPL", "Apple", 100m);
        Limit(account, aapl, OrderSide.Buy, 9m, 100m - 0.01m); // reserves 899.91

        Assert.Throws<DomainException>(() => account.Withdraw(200m, Now));
        var second = Market(account, aapl, OrderSide.Buy, 2m);
        Assert.Equal(OrderStatus.Rejected, second.Status);
    }

    [Fact]
    public void Marketable_limit_buy_fills_immediately_at_market_price()
    {
        var account = FundedAccount(1_000m);
        var aapl = Instrument.Create("AAPL", "Apple", 100m);

        var order = Limit(account, aapl, OrderSide.Buy, 2m, 110m);

        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(100m, order.ExecutedPrice);
        Assert.Equal(0m, account.ReservedCash);
    }

    [Fact]
    public void Pending_limit_buy_fills_when_price_drops_to_limit_and_releases_reservation()
    {
        var account = FundedAccount(1_000m);
        var aapl = Instrument.Create("AAPL", "Apple", 100m);
        var order = Limit(account, aapl, OrderSide.Buy, 5m, 90m);

        Assert.False(account.TryFillPendingOrder(order, 95m, Now));
        Assert.True(account.TryFillPendingOrder(order, 88m, Now));

        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(88m, order.ExecutedPrice);   // better than the limit
        Assert.Equal(0m, account.ReservedCash);
        Assert.Equal(1_000m - 440m, account.CashBalance);
        Assert.Equal(5m, Assert.Single(account.Positions).Quantity);
    }

    [Fact]
    public void Limit_sell_reserves_holdings_and_fills_when_price_rises()
    {
        var account = FundedAccount(1_000m);
        var aapl = Instrument.Create("AAPL", "Apple", 100m);
        Market(account, aapl, OrderSide.Buy, 4m);

        var order = Limit(account, aapl, OrderSide.Sell, 3m, 120m);
        var position = Assert.Single(account.Positions);
        Assert.Equal(3m, position.ReservedQuantity);
        Assert.Equal(OrderStatus.Rejected, Market(account, aapl, OrderSide.Sell, 2m).Status); // only 1 available

        Assert.True(account.TryFillPendingOrder(order, 125m, Now));
        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(1m, position.Quantity);
        Assert.Equal(0m, position.ReservedQuantity);
        Assert.Equal(600m + 375m, account.CashBalance);
    }

    [Fact]
    public void Cancelling_pending_order_releases_reservation()
    {
        var account = FundedAccount(1_000m);
        var aapl = Instrument.Create("AAPL", "Apple", 100m);
        var order = Limit(account, aapl, OrderSide.Buy, 5m, 90m);

        account.CancelOrder(order, Now);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(0m, account.ReservedCash);
        Assert.Throws<DomainException>(() => account.CancelOrder(order, Now));
    }

    [Fact]
    public void Limit_order_without_enough_funds_is_rejected()
    {
        var account = FundedAccount(100m);
        var aapl = Instrument.Create("AAPL", "Apple", 100m);

        var order = Limit(account, aapl, OrderSide.Buy, 5m, 90m);

        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal(0m, account.ReservedCash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Limit_order_requires_positive_limit_price(int? limit)
    {
        var account = FundedAccount(1_000m);
        var aapl = Instrument.Create("AAPL", "Apple", 100m);

        Assert.Throws<DomainException>(() =>
            account.PlaceOrder(aapl, OrderType.Limit, OrderSide.Buy, 1m, limit, Now));
    }

    // ---- Ledger ----

    [Fact]
    public void Every_cash_change_is_recorded_in_the_ledger()
    {
        var account = Account.Open(Owner, "Main", Now);
        var aapl = Instrument.Create("AAPL", "Apple", 100m);

        account.Deposit(1_000m, Now);
        Market(account, aapl, OrderSide.Buy, 2m);
        Market(account, aapl, OrderSide.Sell, 1m);
        account.Withdraw(50m, Now);

        var ledger = account.DequeueNewTransactions();
        Assert.Equal(
            [TransactionType.Deposit, TransactionType.Buy, TransactionType.Sell, TransactionType.Withdrawal],
            ledger.Select(t => t.Type));
        Assert.Equal([1_000m, -200m, 100m, -50m], ledger.Select(t => t.Amount));
        Assert.Equal(850m, ledger[^1].BalanceAfter);
        Assert.Equal(account.CashBalance, ledger[^1].BalanceAfter);
        Assert.Empty(account.DequeueNewTransactions());
    }

    // ---- Validation ----

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_quantity_throws(decimal quantity)
    {
        var account = FundedAccount(1_000m);
        var aapl = Instrument.Create("AAPL", "Apple", 100m);

        Assert.Throws<DomainException>(() => Market(account, aapl, OrderSide.Buy, quantity));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(0.00001)]
    public void Invalid_deposit_amount_throws(decimal amount)
    {
        var account = Account.Open(Owner, "Main", Now);

        Assert.Throws<DomainException>(() => account.Deposit(amount, Now));
    }

    [Fact]
    public void Withdrawing_more_than_balance_throws()
    {
        var account = FundedAccount(50m);

        Assert.Throws<DomainException>(() => account.Withdraw(51m, Now));
        Assert.Equal(50m, account.CashBalance);
    }
}
