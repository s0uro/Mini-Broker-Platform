using Microsoft.EntityFrameworkCore;
using MiniBroker.Domain.Accounts;
using MiniBroker.Domain.Instruments;
using MiniBroker.Domain.Orders;
using MiniBroker.Domain.Users;

namespace MiniBroker.Infrastructure.Persistence;

public sealed class BrokerDbContext(DbContextOptions<BrokerDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();
    public DbSet<Instrument> Instruments => Set<Instrument>();
    public DbSet<PriceTick> PriceTicks => Set<PriceTick>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<WatchlistItem> WatchlistItems => Set<WatchlistItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BrokerDbContext).Assembly);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        CollectLedgerEntries();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        CollectLedgerEntries();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // Accounts record ledger entries in memory; persist them in the same unit of work as the account change.
    private void CollectLedgerEntries()
    {
        foreach (var entry in ChangeTracker.Entries<Account>().ToList())
        {
            foreach (var transaction in entry.Entity.DequeueNewTransactions())
                CashTransactions.Add(transaction);
        }
    }
}
