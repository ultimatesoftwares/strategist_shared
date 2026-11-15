# shared/Persistence — Shared Database Schema

EF Core models for the shared Strategist schema: the server's `AppDbContext`
(+ Identity). Assembly/namespaces stay `Repository.Persistence` /
`Server.Data` until the M9 rename.

Migrations live here too (`Migrations/`, EF's default: the assembly that
contains the context), so **any host** referencing this project can apply
them — the Server does on startup, other hosts opt in with
`Database:Migrate=true` via `RepositoryHost.MigrateRepositoryAsync`, which
is concurrency-safe (EF takes a Postgres advisory lock per migration run).

Adding a migration:

    dotnet ef migrations add SomeName --project shared/Persistence

(`DesignTimeDbContextFactory` makes the EF CLI work without a startup
project; commands that open a connection honor `STRATEGIST_DB`.)
