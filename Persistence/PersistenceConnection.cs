using Microsoft.Extensions.Configuration;

namespace Server.Data
{

    /// <summary>Shared resolution of the PostgreSQL connection string, the same
    /// chain every host uses: `db` (compose convention) → Database:Url →
    /// ConnectionStrings:Default → local-dev default.</summary>
    public static class PersistenceConnection
    {
        public static string Resolve(IConfiguration configuration) =>
            configuration["db"]
            ?? configuration["Database:Url"]
            ?? configuration.GetConnectionString("Default")
            ?? "Host=localhost;Port=5432;Database=strategist-db;Username=strategist-user";
    }
}