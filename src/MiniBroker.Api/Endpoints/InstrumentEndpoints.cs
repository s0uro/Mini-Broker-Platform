using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MiniBroker.Api.Market;
using MiniBroker.Domain.Common;
using MiniBroker.Infrastructure.Persistence;

namespace MiniBroker.Api.Endpoints;

public static class InstrumentEndpoints
{
    private static readonly TimeSpan ChangeWindow = TimeSpan.FromHours(24);

    // Range → (window, number of buckets the series is downsampled to).
    private static readonly Dictionary<string, (TimeSpan Window, int Buckets)> Ranges = new(StringComparer.OrdinalIgnoreCase)
    {
        ["15m"] = (TimeSpan.FromMinutes(15), 180),
        ["1h"] = (TimeSpan.FromHours(1), 240),
        ["1d"] = (TimeSpan.FromDays(1), 288),
    };

    public static IEndpointRouteBuilder MapInstrumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/instruments").WithTags("Instruments").RequireAuthorization();

        group.MapGet("/", List);
        group.MapGet("/{symbol}", Get);
        group.MapGet("/{symbol}/history", History);
        // Lets you move a price by hand (in addition to the simulator); triggers limit-order matching.
        group.MapPut("/{symbol}/price", UpdatePrice);

        return app;
    }

    private static async Task<Ok<List<InstrumentResponse>>> List(
        BrokerDbContext db, TimeProvider clock, CancellationToken ct, string? search = null)
    {
        var query = db.Instruments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(i => i.Symbol.Contains(term) || i.Name.Contains(term));
        }

        var instruments = await query.OrderBy(i => i.Symbol).ToListAsync(ct);
        var references = await ReferencePricesAsync(db, clock.GetUtcNow() - ChangeWindow, ct);
        return TypedResults.Ok(instruments
            .Select(i => InstrumentResponse.From(i, references.GetValueOrDefault(i.Symbol)))
            .ToList());
    }

    private static async Task<Results<Ok<InstrumentResponse>, NotFound>> Get(
        string symbol, BrokerDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var instrument = await db.Instruments.FindAsync([symbol.ToUpperInvariant()], ct);
        if (instrument is null)
            return TypedResults.NotFound();

        var references = await ReferencePricesAsync(db, clock.GetUtcNow() - ChangeWindow, ct, instrument.Symbol);
        return TypedResults.Ok(InstrumentResponse.From(instrument, references.GetValueOrDefault(instrument.Symbol)));
    }

    private static async Task<Results<Ok<List<PricePointResponse>>, NotFound>> History(
        string symbol, BrokerDbContext db, TimeProvider clock, CancellationToken ct, string range = "1h")
    {
        if (!Ranges.TryGetValue(range, out var spec))
            throw new DomainException($"Unknown range '{range}'. Use one of: {string.Join(", ", Ranges.Keys)}.");

        symbol = symbol.ToUpperInvariant();
        if (!await db.Instruments.AnyAsync(i => i.Symbol == symbol, ct))
            return TypedResults.NotFound();

        var since = clock.GetUtcNow() - spec.Window;
        var ticks = await db.PriceTicks.AsNoTracking()
            .Where(t => t.Symbol == symbol && t.Timestamp >= since)
            .OrderBy(t => t.Timestamp)
            .Select(t => new PricePointResponse(t.Timestamp, t.Price))
            .ToListAsync(ct);

        return TypedResults.Ok(Downsample(ticks, since, spec.Window, spec.Buckets));
    }

    private static async Task<Results<Ok<InstrumentResponse>, NotFound>> UpdatePrice(
        string symbol, UpdatePriceRequest request, BrokerDbContext db, PriceService prices, CancellationToken ct)
    {
        symbol = symbol.ToUpperInvariant();
        if (!await db.Instruments.AnyAsync(i => i.Symbol == symbol, ct))
            return TypedResults.NotFound();

        await prices.ApplyPricesAsync(new Dictionary<string, decimal> { [symbol] = request.Price }, ct);
        var instrument = await db.Instruments.AsNoTracking().SingleAsync(i => i.Symbol == symbol, ct);
        return TypedResults.Ok(InstrumentResponse.From(instrument));
    }

    /// <summary>Oldest price per symbol since <paramref name="since"/> — the baseline for the change %.</summary>
    private static async Task<Dictionary<string, decimal>> ReferencePricesAsync(
        BrokerDbContext db, DateTimeOffset since, CancellationToken ct, string? onlySymbol = null)
    {
        var window = db.PriceTicks.Where(t => t.Timestamp >= since);
        if (onlySymbol is not null)
            window = window.Where(t => t.Symbol == onlySymbol);

        var firstTimes = window.GroupBy(t => t.Symbol).Select(g => new { Symbol = g.Key, Timestamp = g.Min(t => t.Timestamp) });
        var rows = await (
            from t in db.PriceTicks
            join f in firstTimes on new { t.Symbol, t.Timestamp } equals new { f.Symbol, f.Timestamp }
            select new { t.Symbol, t.Price }).ToListAsync(ct);

        return rows.GroupBy(r => r.Symbol).ToDictionary(g => g.Key, g => g.First().Price);
    }

    /// <summary>Keeps the last price in each time bucket so long ranges stay a few hundred points.</summary>
    private static List<PricePointResponse> Downsample(List<PricePointResponse> ticks, DateTimeOffset since, TimeSpan window, int buckets)
    {
        if (ticks.Count <= buckets)
            return ticks;

        var bucketTicks = window.Ticks / buckets;
        return ticks
            .GroupBy(t => Math.Min(buckets - 1, (t.Timestamp - since).Ticks / bucketTicks))
            .Select(g => g.Last())
            .ToList();
    }
}
