using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Strategist.Infrastructure.TimeSeries;
using Strategist.TimeSeries;
using Server.Data;

namespace Strategist.Infrastructure.Storage
{

    /// <summary>
    /// The storage access module any service can host: the PostgreSQL context
    /// (the server's <see cref="AppDbContext"/>) and the QuestDB historical
    /// market-data store (<see cref="QuestDb"/>). The hosting service calls
    /// <see cref="AddRepository"/> while building its host and resolves
    /// IDbContextFactory&lt;AppDbContext&gt; or <see cref="QuestDb"/> wherever
    /// it needs storage. Messaging rides the MassTransit bus
    /// (<see cref="Strategist.Infrastructure.Messaging.StrategistBus"/>) since hexagonal P2.
    /// </summary>
    public static class RepositoryHost
    {
        /// <summary>Registers the database context factory and the QuestDB store
        /// on the service collection being built. Factories (not scoped contexts)
        /// so background services can create contexts safely; AddDbContextFactory
        /// also lets scoped consumers inject the context directly.</summary>
        public static IServiceCollection AddRepository(
            this IServiceCollection services, IConfiguration configuration)
        {
            string connectionString = ConnectionString(configuration);
            services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(connectionString));
            services.AddSingleton<QuestDb>();
            return services;
        }

        /// <summary>Database connection string, resolved the same way the other
        /// hosts do: `db` (compose convention) → Database:Url →
        /// ConnectionStrings:Default → local-dev default.</summary>
        public static string ConnectionString(IConfiguration configuration) =>
            PersistenceConnection.Resolve(configuration);

        /// <summary>
        /// Applies the shared schema's EF migrations. They live in the
        /// Persistence assembly next to <see cref="AppDbContext"/>, so ANY
        /// host that registered the repository can be the migrator — not just
        /// the Server. Safe to call from several hosts booting in parallel:
        /// EF takes a Postgres advisory lock for the duration of the run, so
        /// concurrent migrators serialize and the losers see nothing pending.
        /// Retries for ~2 minutes while the db container (or its DNS name)
        /// comes up instead of crashing into a restart loop.
        /// </summary>
        public static async Task MigrateRepositoryAsync(
            this IServiceProvider services, CancellationToken ct = default)
        {
            var logger = services.GetService<ILoggerFactory>()
                ?.CreateLogger("Strategist.Infrastructure.Storage.RepositoryMigrations");
            var factory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await factory.CreateDbContextAsync(ct);
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await db.Database.MigrateAsync(ct);
                    break;
                }
                catch (Exception e) when (attempt < 40 &&
                    (e is Npgsql.NpgsqlException or System.Net.Sockets.SocketException))
                {
                    logger?.LogWarning(
                        "Database not ready (attempt {Attempt}/40): {Error} — retrying in 3s",
                        attempt, e.Message);
                    await Task.Delay(TimeSpan.FromSeconds(3), ct);
                }
            }
        }
    }
}
