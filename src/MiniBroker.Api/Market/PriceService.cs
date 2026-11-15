using Microsoft.EntityFrameworkCore;
using MiniBroker.Domain.Orders;
using MiniBroker.Infrastructure.Persistence;

namespace MiniBroker.Api.Market;

/// <summary>
/// Applies new prices: updates instruments, records price history, and executes any pending
/// limit orders that the new prices make marketable — all in one save.
/// </summary>
public sealed class PriceService(BrokerDbContext db, TimeProvider clock)
{
    /// <returns>Number of pending orders that were executed (or rejected) by this price change.</returns>
    public async Task<int> ApplyPricesAsync(IReadOnlyDictionary<string, decimal> prices, CancellationToken ct)
    {
        if (prices.Count == 0)
            return 0;

        var now = clock.GetUtcNow();
        var symbols = prices.Keys.ToList();

        var instruments = await db.Instruments.Where(i => symbols.Contains(i.Symbol)).ToListAsync(ct);
        foreach (var instrument in instruments)
            db.PriceTicks.Add(instrument.SetPrice(prices[instrument.Symbol], now));

        var pending = await db.Orders
            .Where(o => symbols.Contains(o.Symbol) && o.Status == OrderStatus.Pending)
            .ToListAsync(ct);
        var marketable = pending.Where(o => o.IsMarketableAt(prices[o.Symbol])).OrderBy(o => o.CreatedAt).ToList();

        var processed = 0;
        if (marketable.Count > 0)
        {
            var accountIds = marketable.Select(o => o.AccountId).Distinct().ToList();
            var accounts = await db.Accounts
                .Include(a => a.Positions)
                .Where(a => accountIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, ct);

            foreach (var order in marketable)
            {
                if (accounts.TryGetValue(order.AccountId, out var account) &&
                    account.TryFillPendingOrder(order, prices[order.Symbol], now))
                {
                    processed++;
                }
            }
        }

        await db.SaveChangesAsync(ct);
        return processed;
    }
}
