using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniBroker.Domain.Accounts;
using MiniBroker.Domain.Instruments;
using MiniBroker.Domain.Orders;
using MiniBroker.Domain.Users;

namespace MiniBroker.Infrastructure.Persistence;

internal static class Precision
{
    public const int Digits = 18;
    public const int SymbolLength = 16;
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
        builder.HasIndex(u => u.Email).IsUnique();
        builder.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
    }
}

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Name).HasMaxLength(100).IsRequired();
        builder.Property(a => a.CashBalance).HasPrecision(Precision.Digits, Money.Scale);
        builder.Property(a => a.ReservedCash).HasPrecision(Precision.Digits, Money.Scale);
        builder.Property(a => a.RowVersion).IsRowVersion();
        builder.Ignore(a => a.AvailableCash);

        builder.HasOne<User>().WithMany().HasForeignKey(a => a.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => a.OwnerUserId);

        builder.HasMany(a => a.Positions)
            .WithOne()
            .HasForeignKey(p => p.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.Positions).HasField("_positions");
    }
}

internal sealed class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.ToTable("Positions");
        builder.HasKey(p => new { p.AccountId, p.Symbol });
        builder.Property(p => p.Symbol).HasMaxLength(Precision.SymbolLength);
        builder.Property(p => p.Quantity).HasPrecision(Precision.Digits, Money.QuantityScale);
        builder.Property(p => p.ReservedQuantity).HasPrecision(Precision.Digits, Money.QuantityScale);
        builder.Property(p => p.AveragePrice).HasPrecision(Precision.Digits, Money.Scale);
        builder.Ignore(p => p.AvailableQuantity);
        builder.HasOne<Instrument>().WithMany().HasForeignKey(p => p.Symbol).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CashTransactionConfiguration : IEntityTypeConfiguration<CashTransaction>
{
    public void Configure(EntityTypeBuilder<CashTransaction> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.Amount).HasPrecision(Precision.Digits, Money.Scale);
        builder.Property(t => t.BalanceAfter).HasPrecision(Precision.Digits, Money.Scale);
        builder.Property(t => t.Symbol).HasMaxLength(Precision.SymbolLength);
        builder.Property(t => t.Quantity).HasPrecision(Precision.Digits, Money.QuantityScale);
        builder.Property(t => t.Price).HasPrecision(Precision.Digits, Money.Scale);

        builder.HasOne<Account>().WithMany().HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => new { t.AccountId, t.CreatedAt });
    }
}

internal sealed class InstrumentConfiguration : IEntityTypeConfiguration<Instrument>
{
    public void Configure(EntityTypeBuilder<Instrument> builder)
    {
        builder.HasKey(i => i.Symbol);
        builder.Property(i => i.Symbol).HasMaxLength(Precision.SymbolLength);
        builder.Property(i => i.Name).HasMaxLength(200).IsRequired();
        builder.Property(i => i.LastPrice).HasPrecision(Precision.Digits, Money.Scale);

        builder.HasData(
            new { Symbol = "AAPL", Name = "Apple Inc.", LastPrice = 190.00m },
            new { Symbol = "MSFT", Name = "Microsoft Corp.", LastPrice = 420.00m },
            new { Symbol = "NVDA", Name = "NVIDIA Corp.", LastPrice = 120.00m },
            new { Symbol = "TSLA", Name = "Tesla Inc.", LastPrice = 250.00m },
            new { Symbol = "AMZN", Name = "Amazon.com Inc.", LastPrice = 185.00m },
            new { Symbol = "GOOGL", Name = "Alphabet Inc.", LastPrice = 170.00m },
            new { Symbol = "META", Name = "Meta Platforms Inc.", LastPrice = 500.00m },
            new { Symbol = "NFLX", Name = "Netflix Inc.", LastPrice = 650.00m },
            new { Symbol = "AMD", Name = "Advanced Micro Devices", LastPrice = 160.00m },
            new { Symbol = "JPM", Name = "JPMorgan Chase & Co.", LastPrice = 200.00m },
            new { Symbol = "V", Name = "Visa Inc.", LastPrice = 275.00m },
            new { Symbol = "KO", Name = "Coca-Cola Co.", LastPrice = 62.00m },
            new { Symbol = "DIS", Name = "Walt Disney Co.", LastPrice = 100.00m },
            new { Symbol = "NKE", Name = "Nike Inc.", LastPrice = 95.00m });
    }
}

internal sealed class PriceTickConfiguration : IEntityTypeConfiguration<PriceTick>
{
    public void Configure(EntityTypeBuilder<PriceTick> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();
        builder.Property(t => t.Symbol).HasMaxLength(Precision.SymbolLength).IsRequired();
        builder.Property(t => t.Price).HasPrecision(Precision.Digits, Money.Scale);
        builder.HasOne<Instrument>().WithMany().HasForeignKey(t => t.Symbol).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => new { t.Symbol, t.Timestamp });
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Symbol).HasMaxLength(Precision.SymbolLength).IsRequired();
        builder.Property(o => o.Type).HasConversion<string>().HasMaxLength(8);
        builder.Property(o => o.Side).HasConversion<string>().HasMaxLength(8);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(o => o.Quantity).HasPrecision(Precision.Digits, Money.QuantityScale);
        builder.Property(o => o.LimitPrice).HasPrecision(Precision.Digits, Money.Scale);
        builder.Property(o => o.ExecutedPrice).HasPrecision(Precision.Digits, Money.Scale);
        builder.Property(o => o.RejectionReason).HasMaxLength(500);
        builder.Property(o => o.RowVersion).IsRowVersion();
        builder.Ignore(o => o.IsPending);

        builder.HasOne<Account>().WithMany().HasForeignKey(o => o.AccountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Instrument>().WithMany().HasForeignKey(o => o.Symbol).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(o => new { o.AccountId, o.CreatedAt });
        // Used by the matcher to find resting limit orders on each price change.
        builder.HasIndex(o => new { o.Symbol, o.Status });
    }
}

internal sealed class WatchlistItemConfiguration : IEntityTypeConfiguration<WatchlistItem>
{
    public void Configure(EntityTypeBuilder<WatchlistItem> builder)
    {
        builder.HasKey(w => new { w.UserId, w.Symbol });
        builder.Property(w => w.Symbol).HasMaxLength(Precision.SymbolLength);
        builder.HasOne<User>().WithMany().HasForeignKey(w => w.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Instrument>().WithMany().HasForeignKey(w => w.Symbol).OnDelete(DeleteBehavior.Cascade);
    }
}
