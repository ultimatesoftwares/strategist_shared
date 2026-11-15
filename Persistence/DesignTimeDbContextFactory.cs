using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Server.Data;

/// <summary>
/// Design-time factory for the EF CLI, so migrations can be added against
/// this assembly without booting a host:
///
///   dotnet ef migrations add SomeName --project shared/Persistence
///
/// `migrations add` never opens a connection; commands that do (e.g.
/// `database update`) use STRATEGIST_DB when set, else the local-dev
/// default from <see cref="PersistenceConnection"/>.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("STRATEGIST_DB")
                ?? "Host=localhost;Port=5432;Database=strategist-db;Username=strategist-user")
            .Options);
}
