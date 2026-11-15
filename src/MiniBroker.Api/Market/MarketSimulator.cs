using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MiniBroker.Domain.Instruments;
using MiniBroker.Infrastructure.Persistence;

namespace MiniBroker.Api.Market;

public sealed class MarketOptions
{
    public const string SectionName = "Market";

    /// <summary>Moves prices randomly in the background. Off by default; on in Development.</summary>
    public bool SimulatorEnabled { get; set; }

    public double TickSeconds { get; set; } = 3;

    /// <summary>Standard deviation of each tick's relative move (0.0004 = 0.04%).</summary>
    public double Volatility { get; set; } = 0.0004;

    /// <summary>On first start with no history, generate the last 24h so charts aren't empty.</summary>
    public bool BackfillHistory { get; set; } = true;

    public int HistoryRetentionHours { get; set; } = 26;
}

/// <summary>
/// A random-walk price feed for demos. Each tick goes through <see cref="PriceService"/>, so it also
/// fills limit orders exactly like a real price update would.
/// </summary>
public sealed class MarketSimulator(
    IServiceScopeFactory scopes,
    IOptions<MarketOptions> options,
    TimeProvider clock,
    ILogger<MarketSimulator> logger) : BackgroundService
{
    private static readonly TimeSpan BackfillStep = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PruneEvery = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.SimulatorEnabled)
            return;

        logger.LogInformation("Market simulator running every {Seconds}s", o.TickSeconds);
        var lastPrune = DateTimeOffset.MinValue;

        try
        {
            if (o.BackfillHistory)
                await BackfillAsync(o, stoppingToken);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(o.TickSeconds), clock);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await TickAsync(o, stoppingToken);
                    if (clock.GetUtcNow() - lastPrune > PruneEvery)
                    {
                        await PruneAsync(o, stoppingToken);
                        lastPrune = clock.GetUtcNow();
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // e.g. a concurrency conflict with a user's order — the next tick retries.
                    logger.LogWarning(ex, "Market tick failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    private async Task TickAsync(MarketOptions o, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerDbContext>();
        var instruments = await db.Instruments.AsNoTracking().ToListAsync(ct);

        var next = instruments.ToDictionary(i => i.Symbol, i => Step(i.LastPrice, o.Volatility));
        var filled = await scope.ServiceProvider.GetRequiredService<PriceService>().ApplyPricesAsync(next, ct);
        if (filled > 0)
            logger.LogInformation("Executed {Count} limit order(s)", filled);
    }

    private async Task BackfillAsync(MarketOptions o, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerDbContext>();
        if (await db.PriceTicks.AnyAsync(ct))
            return;

        var now = clock.GetUtcNow();
        var steps = (int)(TimeSpan.FromHours(24) / BackfillStep);
        // Scale per-tick volatility up to the backfill step (random walk: σ grows with √time).
        var stepVolatility = o.Volatility * Math.Sqrt(BackfillStep.TotalSeconds / o.TickSeconds);

        foreach (var instrument in await db.Instruments.AsNoTracking().ToListAsync(ct))
        {
            // Walk backwards from today's price so the history ends exactly at the current price.
            var price = instrument.LastPrice;
            for (var i = 1; i <= steps; i++)
            {
                price = Step(price, stepVolatility);
                db.PriceTicks.Add(new PriceTick(instrument.Symbol, price, now - BackfillStep * i));
            }
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Backfilled 24h of price history");
    }

    private async Task PruneAsync(MarketOptions o, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerDbContext>();
        var cutoff = clock.GetUtcNow().AddHours(-o.HistoryRetentionHours);
        var deleted = await db.PriceTicks.Where(t => t.Timestamp < cutoff).ExecuteDeleteAsync(ct);
        if (deleted > 0)
            logger.LogInformation("Pruned {Count} old price ticks", deleted);
    }

    private static decimal Step(decimal price, double volatility)
    {
        // Box–Muller: a standard normal sample from two uniforms.
        var u1 = 1.0 - Random.Shared.NextDouble();
        var u2 = Random.Shared.NextDouble();
        var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);

        var next = (double)price * (1 + volatility * z);
        return Math.Max(0.01m, Math.Round((decimal)next, 2));
    }
}
