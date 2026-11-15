using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MiniBroker.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef migrations add`. Generating a migration never connects to the database.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BrokerDbContext>
{
    public BrokerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BrokerDbContext>()
            .UseSqlServer("Server=localhost,1433;Database=MiniBroker;TrustServerCertificate=True")
            .Options;
        return new BrokerDbContext(options);
    }
}
