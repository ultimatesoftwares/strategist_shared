using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Server.Models;

namespace Server.Data
{
    /// <summary>
    /// Normalizes every DateTime written to PostgreSQL to UTC: 'timestamp with
    /// time zone' columns reject Kind=Unspecified values (e.g. from JSON bodies
    /// or form inputs without an offset), so Unspecified is treated as UTC and
    /// Local is converted. Values read back are stamped as UTC.
    /// </summary>
    public class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(v, DateTimeKind.Utc)
            : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));


    ///
    public class AppDbContext(DbContextOptions<AppDbContext> options)
        : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
    {
        public DbSet<Address> Addresses => Set<Address>();
        public DbSet<UserInformation> UserInformation => Set<UserInformation>();

        public DbSet<Order> Orders => Set<Order>();
        public DbSet<CTraderVenue> CTraderVenues => Set<CTraderVenue>();
        public DbSet<Instrument> Instruments => Set<Instrument>();
        public DbSet<InstrumentArtwork> InstrumentArtworks => Set<InstrumentArtwork>();

        public DbSet<ModelDecision> ModelDecisions => Set<ModelDecision>();
        public DbSet<InstrumentCatalogEntry> InstrumentCatalog => Set<InstrumentCatalogEntry>();
        public DbSet<StrategyModel> StrategyModels => Set<StrategyModel>();
        public DbSet<StrategyDeployment> StrategyDeployments => Set<StrategyDeployment>();
        public DbSet<TradingAccountProfile> TradingAccountProfiles => Set<TradingAccountProfile>();
        public DbSet<BacktestResult> BacktestResults => Set<BacktestResult>();
        public DbSet<ProvisionJob> ProvisionJobs => Set<ProvisionJob>();

        public DbSet<Project> Projects => Set<Project>();
        public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
        public DbSet<ProjectDocument> ProjectDocuments => Set<ProjectDocument>();
        public DbSet<ProjectRiskPolicy> ProjectRiskPolicies => Set<ProjectRiskPolicy>();
        public DbSet<ProjectStrategyLink> ProjectStrategyLinks => Set<ProjectStrategyLink>();
        public DbSet<ProjectActivity> ProjectActivity => Set<ProjectActivity>();

        // Fleet venue-host registry (src/Fleet hub).
        public DbSet<FleetTerminal> FleetTerminals => Set<FleetTerminal>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            base.ConfigureConventions(configurationBuilder);
            // Applies to every DateTime/DateTime? property model-wide.
            configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
            configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<ApplicationUser>().ToTable("users");
            builder.Entity<Address>().ToTable("addresses");
            builder.Entity<UserInformation>().ToTable("user_information")
                .HasIndex(x => x.UserId).IsUnique();

            builder.Entity<Order>(e =>
            {
                e.ToTable("orders");
                // Strategy-path orders link to the model decision that caused
                // them; SetNull keeps the order row if a decision is deleted.
                e.HasIndex(x => x.DecisionId);
                e.HasIndex(x => new { x.VenueId, x.BrokerPositionTicket });
                e.HasIndex(x => new { x.VenueId, x.BrokerOrderTicket });
                e.HasOne(x => x.Decision).WithMany()
                    .HasForeignKey(x => x.DecisionId)
                    .OnDelete(DeleteBehavior.SetNull);
            });
            // "venues": each row is a
            // broker account credential, not a terminal workload (those live
            // in the fleet hub's registry).
            builder.Entity<CTraderVenue>().ToTable("ctrader_venues");

            builder.Entity<Instrument>(e =>
            {
                e.ToTable("instruments");
                e.HasIndex(x => x.Symbol).IsUnique();
            });
            builder.Entity<InstrumentArtwork>(e =>
            {
                e.ToTable("instrument_artwork");
                e.HasKey(x => x.InstrumentId);
                e.HasOne<Instrument>().WithOne().HasForeignKey<InstrumentArtwork>(x => x.InstrumentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Instrument catalog: every venue-native listing, synced from the
            // brokers; mapped_to links a listing to the registered instrument a
            // user mapped it to (Map Instruments) — the single mapping record.
            // The mapping pipeline reads the DB catalog, never the broker live.
            builder.Entity<InstrumentCatalogEntry>(e =>
            {
                e.ToTable("instrument_catalog");
                e.Property(c => c.MappedTo).HasColumnName("mapped_to");
                e.HasIndex(c => c.MappedTo);
                e.HasOne(c => c.MappedToInstrument).WithMany()
                    .HasForeignKey(c => c.MappedTo)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // Registered ONNX strategy models: content-hash PK, blob on disk,
            // row is the catalog entry.
            builder.Entity<StrategyModel>(e =>
            {
                e.ToTable("strategy_models");
                e.Property(x => x.MinBalanceUsd).HasPrecision(18, 2);
                e.Property(x => x.AccountType).HasDefaultValue("all");
                e.Property(x => x.Venues).HasDefaultValue("");
                e.Property(x => x.Enabled).HasDefaultValue(true);
            });
            builder.Entity<StrategyDeployment>(e =>
            {
                e.ToTable("strategy_deployments");
                e.HasIndex(x => x.ModelId);
                e.HasOne<StrategyModel>().WithMany().HasForeignKey(x => x.ModelId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
            builder.Entity<TradingAccountProfile>(e => e.ToTable("trading_account_profiles"));

            // Strategy decision journal: bot-written (OrderLog) for
            // evaluations that emitted orders; orders.DecisionId links back.
            builder.Entity<ModelDecision>(e => e.ToTable("model_decisions"));

            // Research-engine backtest results — keyed by the job id;
            // summary/report are queryable jsonb.
            builder.Entity<BacktestResult>(e =>
            {
                e.ToTable("backtest_results");
                e.HasIndex(b => b.Status); // the GUI/watchdog filter on status
                e.Property(x => x.SummaryJson).HasColumnType("jsonb");
                e.Property(x => x.ReportJson).HasColumnType("jsonb");
            });

            // Market-data provisioning jobs — the provisioning counterpart of
            // backtest_results: rows survive server restarts and the fleet
            // hub's state frames mirror onto them.
            builder.Entity<ProvisionJob>(e =>
            {
                e.ToTable("provision_jobs");
                e.HasIndex(j => j.Status); // the listing prunes finished rows
            });

            // Collaboration projects: members / documents / risk policies /
            // strategy links / activity die with the project (cascade); user FKs
            // other than membership detach on user deletion (SetNull) so project
            // records and research survive staff churn.
            builder.Entity<Project>(e =>
            {
                e.ToTable("projects");
                e.HasOne(p => p.Owner).WithMany()
                    .HasForeignKey(p => p.OwnerId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<ProjectMember>(e =>
            {
                e.ToTable("project_members");
                e.HasIndex(m => new { m.ProjectId, m.UserId }).IsUnique();
                e.HasOne(m => m.Project).WithMany(p => p.Members)
                    .HasForeignKey(m => m.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(m => m.User).WithMany()
                    .HasForeignKey(m => m.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<ProjectDocument>(e =>
            {
                e.ToTable("project_documents");
                e.HasIndex(d => d.ProjectId);
                e.HasOne(d => d.Project).WithMany()
                    .HasForeignKey(d => d.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(d => d.UploadedBy).WithMany()
                    .HasForeignKey(d => d.UploadedById)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<ProjectRiskPolicy>(e =>
            {
                e.ToTable("project_risk_policies");
                e.HasIndex(r => r.ProjectId);
                e.Property(x => x.RulesJson).HasColumnType("jsonb");
                e.HasOne(r => r.Project).WithMany()
                    .HasForeignKey(r => r.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(r => r.Author).WithMany()
                    .HasForeignKey(r => r.AuthorId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<ProjectStrategyLink>(e =>
            {
                e.ToTable("project_strategy_links");
                e.HasIndex(l => new { l.ProjectId, l.ModelId }).IsUnique();
                e.HasOne(l => l.Project).WithMany()
                    .HasForeignKey(l => l.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(l => l.LinkedBy).WithMany()
                    .HasForeignKey(l => l.LinkedById)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            builder.Entity<ProjectActivity>(e =>
            {
                e.ToTable("project_activity");
                e.HasIndex(a => a.ProjectId);
                e.HasOne(a => a.Project).WithMany()
                    .HasForeignKey(a => a.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(a => a.User).WithMany()
                    .HasForeignKey(a => a.UserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // fleetd's terminal registry: each fleetd host loads only its own
            // rows (HostId), so several fleet VMs can share the schema.
            builder.Entity<FleetTerminal>(e =>
            {
                e.ToTable("fleet_terminals");
                e.HasIndex(t => t.HostId);
                e.Property(t => t.ArgsJson).HasColumnType("jsonb");
                e.Property(t => t.EnvironmentJson).HasColumnType("jsonb");
            });

            // Store enums as strings for readability.
            foreach (var entityType in builder.Model.GetEntityTypes())
            foreach (var property in entityType.GetProperties())
                if (property.ClrType.IsEnum)
                    property.SetProviderClrType(typeof(string));
        }

        public override int SaveChanges()
        {
            Timestamp();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Timestamp();
            return base.SaveChangesAsync(ct);
        }

        private void Timestamp()
        {
            var now = DateTime.UtcNow;
            foreach (var entry in ChangeTracker.Entries<BaseEntity>())
            {
                if (entry.State == EntityState.Added) entry.Entity.CreatedAt = now;
                if (entry.State is EntityState.Added or EntityState.Modified)
                    entry.Entity.UpdatedAt = now;
            }
        }
    }
}
