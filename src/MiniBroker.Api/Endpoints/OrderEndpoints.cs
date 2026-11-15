using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MiniBroker.Api.Auth;
using MiniBroker.Domain.Orders;
using MiniBroker.Infrastructure.Persistence;

namespace MiniBroker.Api.Endpoints;

public static class OrderEndpoints
{
    private const int MaxOrders = 500;

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounts/{accountId:guid}/orders").WithTags("Orders").RequireAuthorization();

        group.MapPost("/", Place);
        group.MapGet("/", List);
        group.MapDelete("/{orderId:guid}", Cancel);

        return app;
    }

    private static async Task<Results<Created<OrderResponse>, NotFound<ProblemDetailsLite>>> Place(
        Guid accountId, PlaceOrderRequest request, HttpContext http, BrokerDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var account = await AccountEndpoints.LoadOwnedAsync(db, accountId, http.User.GetUserId(), ct);
        if (account is null)
            return TypedResults.NotFound(new ProblemDetailsLite("Account not found."));

        var symbol = (request.Symbol ?? string.Empty).Trim().ToUpperInvariant();
        var instrument = await db.Instruments.FindAsync([symbol], ct);
        if (instrument is null)
            return TypedResults.NotFound(new ProblemDetailsLite($"Instrument '{symbol}' not found."));

        var order = account.PlaceOrder(instrument, request.Type, request.Side, request.Quantity, request.LimitPrice, clock.GetUtcNow());
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/accounts/{accountId}/orders/{order.Id}", OrderResponse.From(order));
    }

    private static async Task<Results<Ok<List<OrderResponse>>, NotFound>> List(
        Guid accountId, HttpContext http, BrokerDbContext db, CancellationToken ct, OrderStatus? status = null, int take = 200)
    {
        if (!await AccountEndpoints.OwnsAccountAsync(db, accountId, http.User.GetUserId(), ct))
            return TypedResults.NotFound();

        var query = db.Orders.AsNoTracking().Where(o => o.AccountId == accountId);
        if (status is { } s)
            query = query.Where(o => o.Status == s);

        var orders = await query.OrderByDescending(o => o.CreatedAt).Take(Math.Clamp(take, 1, MaxOrders)).ToListAsync(ct);
        return TypedResults.Ok(orders.Select(OrderResponse.From).ToList());
    }

    private static async Task<Results<Ok<OrderResponse>, NotFound>> Cancel(
        Guid accountId, Guid orderId, HttpContext http, BrokerDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var account = await AccountEndpoints.LoadOwnedAsync(db, accountId, http.User.GetUserId(), ct);
        var order = account is null
            ? null
            : await db.Orders.SingleOrDefaultAsync(o => o.Id == orderId && o.AccountId == accountId, ct);
        if (account is null || order is null)
            return TypedResults.NotFound();

        account.CancelOrder(order, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(OrderResponse.From(order));
    }
}
