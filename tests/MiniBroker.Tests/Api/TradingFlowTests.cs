using System.Net;
using System.Net.Http.Json;
using MiniBroker.Api;
using MiniBroker.Domain.Accounts;
using MiniBroker.Domain.Orders;

namespace MiniBroker.Tests.Api;

public class TradingFlowTests(BrokerApiFactory factory) : IClassFixture<BrokerApiFactory>
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = BrokerApiFactory.Json;

    private static async Task<T> Read<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    private static async Task<AccountResponse> OpenFundedAccountAsync(HttpClient client, decimal cash)
    {
        var created = await client.PostAsJsonAsync("/api/accounts", new OpenAccountRequest("Main"), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var account = await Read<AccountResponse>(created);

        var deposit = await client.PostAsJsonAsync($"/api/accounts/{account.Id}/deposits", new MoneyRequest(cash), Json);
        deposit.EnsureSuccessStatusCode();
        return await Read<AccountResponse>(deposit);
    }

    private static Task<HttpResponseMessage> PlaceAsync(HttpClient client, Guid accountId, PlaceOrderRequest request) =>
        client.PostAsJsonAsync($"/api/accounts/{accountId}/orders", request, Json);

    [Fact]
    public async Task Seeded_instruments_are_listed_and_searchable()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var all = await client.GetFromJsonAsync<List<InstrumentResponse>>("/api/instruments", Json);
        Assert.Contains(all!, i => i.Symbol == "AAPL");
        Assert.True(all!.Count >= 10);

        var search = await client.GetFromJsonAsync<List<InstrumentResponse>>("/api/instruments?search=NVD", Json);
        Assert.Equal("NVDA", Assert.Single(search!).Symbol);
    }

    [Fact]
    public async Task Market_buy_is_filled_and_reflected_in_account_and_ledger()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var account = await OpenFundedAccountAsync(client, 1_000m);
        var price = (await client.GetFromJsonAsync<InstrumentResponse>("/api/instruments/AAPL", Json))!.LastPrice;

        var response = await PlaceAsync(client, account.Id, new PlaceOrderRequest("aapl", OrderSide.Buy, 2m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await Read<OrderResponse>(response);
        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(OrderType.Market, order.Type);

        var updated = await client.GetFromJsonAsync<AccountResponse>($"/api/accounts/{account.Id}", Json);
        Assert.Equal(1_000m - 2 * price, updated!.CashBalance);
        Assert.Equal(2m, Assert.Single(updated.Positions).Quantity);

        var ledger = await client.GetFromJsonAsync<List<TransactionResponse>>($"/api/accounts/{account.Id}/transactions", Json);
        Assert.Equal([TransactionType.Buy, TransactionType.Deposit], ledger!.Select(t => t.Type)); // newest first
        Assert.Equal(order.Id, ledger[0].OrderId);
    }

    [Fact]
    public async Task Limit_order_waits_then_fills_when_price_moves()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var account = await OpenFundedAccountAsync(client, 5_000m);
        await client.PutAsJsonAsync("/api/instruments/MSFT/price", new UpdatePriceRequest(400m), Json);

        var placed = await Read<OrderResponse>(await PlaceAsync(client, account.Id,
            new PlaceOrderRequest("MSFT", OrderSide.Buy, 2m, OrderType.Limit, 380m)));
        Assert.Equal(OrderStatus.Pending, placed.Status);

        var reserved = await client.GetFromJsonAsync<AccountResponse>($"/api/accounts/{account.Id}", Json);
        Assert.Equal(760m, reserved!.ReservedCash);
        Assert.Equal(5_000m - 760m, reserved.AvailableCash);

        var pending = await client.GetFromJsonAsync<List<OrderResponse>>($"/api/accounts/{account.Id}/orders?status=Pending", Json);
        Assert.Equal(placed.Id, Assert.Single(pending!).Id);

        // Price drops through the limit -> the matcher fills it at the new market price.
        (await client.PutAsJsonAsync("/api/instruments/MSFT/price", new UpdatePriceRequest(375m), Json)).EnsureSuccessStatusCode();

        var orders = await client.GetFromJsonAsync<List<OrderResponse>>($"/api/accounts/{account.Id}/orders", Json);
        var filled = Assert.Single(orders!, o => o.Id == placed.Id);
        Assert.Equal(OrderStatus.Filled, filled.Status);
        Assert.Equal(375m, filled.ExecutedPrice);

        var after = await client.GetFromJsonAsync<AccountResponse>($"/api/accounts/{account.Id}", Json);
        Assert.Equal(0m, after!.ReservedCash);
        Assert.Equal(5_000m - 750m, after.CashBalance);
    }

    [Fact]
    public async Task Pending_order_can_be_cancelled()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var account = await OpenFundedAccountAsync(client, 1_000m);
        var placed = await Read<OrderResponse>(await PlaceAsync(client, account.Id,
            new PlaceOrderRequest("KO", OrderSide.Buy, 1m, OrderType.Limit, 1m)));

        var cancel = await client.DeleteAsync($"/api/accounts/{account.Id}/orders/{placed.Id}");

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(OrderStatus.Cancelled, (await Read<OrderResponse>(cancel)).Status);
        var after = await client.GetFromJsonAsync<AccountResponse>($"/api/accounts/{account.Id}", Json);
        Assert.Equal(0m, after!.ReservedCash);

        var again = await client.DeleteAsync($"/api/accounts/{account.Id}/orders/{placed.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task Order_without_funds_is_recorded_as_rejected()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var account = await OpenFundedAccountAsync(client, 10m);

        var order = await Read<OrderResponse>(await PlaceAsync(client, account.Id, new PlaceOrderRequest("MSFT", OrderSide.Buy, 1m)));

        Assert.Equal(OrderStatus.Rejected, order.Status);
    }

    [Fact]
    public async Task Price_history_contains_updates()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        await client.PutAsJsonAsync("/api/instruments/NFLX/price", new UpdatePriceRequest(640m), Json);
        await client.PutAsJsonAsync("/api/instruments/NFLX/price", new UpdatePriceRequest(660m), Json);

        var history = await client.GetFromJsonAsync<List<PricePointResponse>>("/api/instruments/NFLX/history?range=1h", Json);

        Assert.Equal([640m, 660m], history!.Select(p => p.Price).TakeLast(2));
        var badRange = await client.GetAsync("/api/instruments/NFLX/history?range=10y");
        Assert.Equal(HttpStatusCode.BadRequest, badRange.StatusCode);
    }

    [Fact]
    public async Task Watchlist_add_list_remove()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync("/api/watchlist/tsla", null)).StatusCode);
        await client.PutAsync("/api/watchlist/TSLA", null); // idempotent
        await client.PutAsync("/api/watchlist/AMD", null);
        Assert.Equal(["TSLA", "AMD"], await client.GetFromJsonAsync<List<string>>("/api/watchlist", Json));

        await client.DeleteAsync("/api/watchlist/TSLA");
        Assert.Equal(["AMD"], await client.GetFromJsonAsync<List<string>>("/api/watchlist", Json));
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsync("/api/watchlist/NOPE", null)).StatusCode);
    }

    [Fact]
    public async Task Invalid_deposit_returns_problem_details()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var account = await OpenFundedAccountAsync(client, 10m);

        var response = await client.PostAsJsonAsync($"/api/accounts/{account.Id}/deposits", new MoneyRequest(-5m), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unknown_instrument_returns_404()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var account = await OpenFundedAccountAsync(client, 100m);

        var response = await PlaceAsync(client, account.Id, new PlaceOrderRequest("NOPE", OrderSide.Buy, 1m));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
