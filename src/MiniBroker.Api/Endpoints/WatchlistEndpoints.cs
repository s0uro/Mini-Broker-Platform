using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MiniBroker.Api.Auth;
using MiniBroker.Domain.Users;
using MiniBroker.Infrastructure.Persistence;

namespace MiniBroker.Api.Endpoints;

public static class WatchlistEndpoints
{
    public static IEndpointRouteBuilder MapWatchlistEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/watchlist").WithTags("Watchlist").RequireAuthorization();

        group.MapGet("/", List);
        group.MapPut("/{symbol}", Add);
        group.MapDelete("/{symbol}", Remove);

        return app;
    }

    private static async Task<Ok<List<string>>> List(HttpContext http, BrokerDbContext db, CancellationToken ct)
    {
        var userId = http.User.GetUserId();
        var symbols = await db.WatchlistItems.AsNoTracking()
            .Where(w => w.UserId == userId)
            .OrderBy(w => w.AddedAt)
            .Select(w => w.Symbol)
            .ToListAsync(ct);
        return TypedResults.Ok(symbols);
    }

    private static async Task<Results<NoContent, NotFound>> Add(
        string symbol, HttpContext http, BrokerDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var userId = http.User.GetUserId();
        symbol = symbol.ToUpperInvariant();
        if (!await db.Instruments.AnyAsync(i => i.Symbol == symbol, ct))
            return TypedResults.NotFound();

        if (!await db.WatchlistItems.AnyAsync(w => w.UserId == userId && w.Symbol == symbol, ct))
        {
            db.WatchlistItems.Add(new WatchlistItem(userId, symbol, clock.GetUtcNow()));
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Added concurrently by another request — the end state is what the caller wanted.
            }
        }
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> Remove(string symbol, HttpContext http, BrokerDbContext db, CancellationToken ct)
    {
        var userId = http.User.GetUserId();
        symbol = symbol.ToUpperInvariant();
        var item = await db.WatchlistItems.SingleOrDefaultAsync(w => w.UserId == userId && w.Symbol == symbol, ct);
        if (item is not null)
        {
            db.WatchlistItems.Remove(item);
            await db.SaveChangesAsync(ct);
        }
        return TypedResults.NoContent();
    }
}
