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
        public DbSet<MetaTraderTerminal> MetaTraderTerminals => Set<MetaTraderTerminal>();
        public DbSet<CTraderVenue> CTraderVenues => Set<CTraderVenue>();
        public DbSet<AlpacaVenue> AlpacaVenues => Set<AlpacaVenue>();
        public DbSet<FleetVenueRegistration> FleetVenueRegistrations => Set<FleetVenueRegistration>();
        public DbSet<Instrument> Instruments => Set<Instrument>();
        public DbSet<StrategyInstance> StrategyInstances => Set<StrategyInstance>();
        public DbSet<VenueInstance> VenueInstances => Set<VenueInstance>();
        public DbSet<FeedInstance> FeedInstances => Set<FeedInstance>();

        public DbSet<VenueOwner> VenueOwners => Set<VenueOwner>();
        public DbSet<InstancePosition> InstancePositions => Set<InstancePosition>();
        public DbSet<ModelDecision> ModelDecisions => Set<ModelDecision>();
        public DbSet<InstrumentCatalogEntry> InstrumentCatalog => Set<InstrumentCatalogEntry>();
        public DbSet<StrategyPackage> StrategyPackages => Set<StrategyPackage>();
        public DbSet<BacktestResult> BacktestResults => Set<BacktestResult>();
        public DbSet<ProvisionJob> ProvisionJobs => Set<ProvisionJob>();

        public DbSet<Project> Projects => Set<Project>();
        public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
        public DbSet<ProjectDocument> ProjectDocuments => Set<ProjectDocument>();
        public DbSet<ProjectRiskPolicy> ProjectRiskPolicies => Set<ProjectRiskPolicy>();
        public DbSet<ProjectStrategyLink> ProjectStrategyLinks => Set<ProjectStrategyLink>();
        public DbSet<ProjectActivity> ProjectActivity => Set<ProjectActivity>();

        // Fleet venue-host registry (src/Fleet hub).
        public DbSet<FleetVenue> FleetVenues => Set<FleetVenue>();
        public DbSet<FleetApiKey> FleetApiKeys => Set<FleetApiKey>();
        public DbSet<FleetInstrumentMapping> FleetInstrumentMappings => Set<FleetInstrumentMapping>();
        public DbSet<FleetVenueInstrument> FleetVenueInstruments => Set<FleetVenueInstrument>();
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
                // Quant-path fills link to the model decision that caused
                // them; the decision-log retention purge never deletes linked
                // rows, so SetNull only fires on manual decision deletes.
                e.HasIndex(x => x.DecisionId);
                e.HasOne(x => x.Decision).WithMany()
                    .HasForeignKey(x => x.DecisionId)
                    .OnDelete(DeleteBehavior.SetNull);
            });
            // "venues" like its ctrader/alpaca siblings: each row is a
            // broker account credential, not a terminal workload (those live
            // in the fleet hub's registry).
            builder.Entity<MetaTraderTerminal>().ToTable("metatrader_venues")
                .HasIndex(x => new { x.Login, x.Server }).IsUnique();
            builder.Entity<CTraderVenue>().ToTable("ctrader_venues");
            builder.Entity<AlpacaVenue>().ToTable("alpaca_venues");
            builder.Entity<FleetVenueRegistration>().ToTable("fleet_venue_registrations")
                .HasIndex(x => x.VenueId).IsUnique();
            builder.Entity<VenueOwner>().ToTable("venue_owner");
            builder.Entity<StrategyInstance>().ToTable("strategy_instances");
            builder.Entity<VenueInstance>().ToTable("venue_instances");
            builder.Entity<FeedInstance>().ToTable("feed_instances");

            builder.Entity<Instrument>(e =>
            {
                e.ToTable("instruments");
                e.HasIndex(x => x.Symbol).IsUnique();
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

            // Cascade instrument → dependents. Venues attach through
            // VenueInstance rows (multiple venues per instance).
            builder.Entity<StrategyInstance>(e =>
            {
                e.ToTable("strategy_instances");
                e.HasOne(s => s.Instrument).WithMany()
                    .HasForeignKey(s => s.InstrumentId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Venue sessions per strategy instance (independence between strategy
            // instances sharing the same venue account); one per selected venue.
            builder.Entity<VenueInstance>(e =>
            {
                e.HasIndex(v => v.StrategyInstanceId);
                e.HasOne(v => v.Venue).WithMany()
                    .HasForeignKey(v => v.VenueId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(v => v.AlpacaVenue).WithMany()
                    .HasForeignKey(v => v.AlpacaVenueId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(v => v.MetaTraderTerminal).WithMany()
                    .HasForeignKey(v => v.MetaTraderTerminalId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(v => v.FleetVenue).WithMany()
                    .HasForeignKey(v => v.FleetVenueId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(v => v.StrategyInstance).WithMany(s => s.VenueInstances)
                    .HasForeignKey(v => v.StrategyInstanceId)
                    .OnDelete(DeleteBehavior.Cascade);
                // The feed can be another venue's: don't cascade, just detach
                // when the feed goes away.
                e.HasOne(v => v.FeedInstance).WithMany()
                    .HasForeignKey(v => v.FeedInstanceId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // A feed instance is owned by (and dies with) the venue instance that
            // spawned it.
            builder.Entity<FeedInstance>(e =>
            {
                e.HasIndex(f => f.VenueInstanceId);
                e.HasOne(f => f.VenueInstance).WithMany()
                    .HasForeignKey(f => f.VenueInstanceId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Immutable validated strategy packages (quant platform M1, D9):
            // content-hash PK, manifest as queryable jsonb. PromotionHistory is
            // the M6 stage-advance audit trail.
            builder.Entity<StrategyPackage>(e =>
            {
                e.ToTable("strategy_packages");
                e.Property(x => x.ManifestJson).HasColumnType("jsonb");
                e.Property(x => x.PromotionHistory).HasColumnType("jsonb");
            });

            // Quant-runtime per-bar decision log (quant platform M6, D6):
            // engine-written fail-open, server-read for replay parity.
            builder.Entity<ModelDecision>(e => e.ToTable("model_decisions"));

            // Quant-package backtest results (quant platform M4, D9) — keyed by
            // the bus job id directly (M4 design freeze); summary/report are
            // queryable jsonb like strategy_packages.ManifestJson.
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
                e.HasIndex(l => new { l.ProjectId, l.PackageId }).IsUnique();
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

            // Fleet venue-host registry (src/Fleet hub): managed venue
            // connections, their revocable API keys, and the canonical-instrument
            // -> venue-symbol mappings that drive quote subscriptions.
            builder.Entity<FleetVenue>(e =>
            {
                e.ToTable("fleet_venues");
                e.HasIndex(t => t.ClientId).IsUnique();
                e.HasIndex(t => t.SidecarToken).IsUnique();
                e.Property(t => t.CredentialsJson).HasColumnType("jsonb");
            });

            builder.Entity<FleetApiKey>(e =>
            {
                e.ToTable("fleet_api_keys");
                e.HasKey(k => k.KeyId);
                e.HasOne(k => k.Venue).WithMany(t => t.ApiKeys)
                    .HasForeignKey(k => k.VenueId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<FleetInstrumentMapping>(e =>
            {
                e.ToTable("fleet_instrument_mappings");
                e.HasIndex(m => new { m.VenueId, m.CanonicalInstrumentId }).IsUnique();
                e.HasOne(m => m.Venue).WithMany()
                    .HasForeignKey(m => m.VenueId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // A venue's instrument catalog, replaced wholesale on every connect
            // (the hub downloads the venue's full symbol catalog).
            builder.Entity<FleetVenueInstrument>(e =>
            {
                e.ToTable("fleet_venue_instruments");
                e.HasIndex(i => new { i.VenueId, i.Symbol }).IsUnique();
                e.HasOne(i => i.Venue).WithMany()
                    .HasForeignKey(i => i.VenueId)
                    .OnDelete(DeleteBehavior.Cascade);
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
            CoupleVenueInstances(now);
            CoupleFeedInstances(now);
            foreach (var entry in ChangeTracker.Entries<BaseEntity>())
            {
                if (entry.State == EntityState.Added) entry.Entity.CreatedAt = now;
                if (entry.State is EntityState.Added or EntityState.Modified)
                    entry.Entity.UpdatedAt = now;
            }
        }

        /// <summary>
        /// Every strategy instance spawns its own venue instances (one per venue
        /// in <see cref="StrategyInstance.Venues"/> — "ctrader:{id}" /
        /// "alpaca:{id}" / "metatrader:{id}" tokens) so instances
        /// stay independent. A non-null Venues list on a tracked instance is
        /// reconciled: missing venues are provisioned, removed ones deleted.
        /// Deleting a strategy instance deletes the venue instances it spawned.
        /// </summary>
        private void CoupleVenueInstances(DateTime now)
        {
            foreach (var entry in ChangeTracker.Entries<StrategyInstance>().ToList())
            {
                if (entry.State == EntityState.Added)
                {
                    foreach (var token in NormalizedVenueTokens(entry.Entity.Venues))
                        Set<VenueInstance>().Add(SpawnVenueInstance(
                            entry.Entity.Id, token, FeedSourceFor(entry.Entity, token),
                            LegsFor(entry.Entity, token)));
                }
                else if (entry.State == EntityState.Deleted)
                {
                    foreach (var vi in Set<VenueInstance>()
                                 .Where(v => v.StrategyInstanceId == entry.Entity.Id).ToList())
                        Set<VenueInstance>().Remove(vi);
                }
                else if (entry.State == EntityState.Modified && entry.Entity.Venues is not null)
                {
                    // Reconcile the desired venue set against the live sessions.
                    var desired = NormalizedVenueTokens(entry.Entity.Venues);
                    foreach (var vi in Set<VenueInstance>()
                                 .Where(v => v.StrategyInstanceId == entry.Entity.Id).ToList())
                    {
                        if (desired.Remove(VenueTokenOf(vi))) continue;
                        Set<VenueInstance>().Remove(vi);
                    }

                    foreach (var token in desired)
                        Set<VenueInstance>().Add(SpawnVenueInstance(
                            entry.Entity.Id, token, FeedSourceFor(entry.Entity, token),
                            LegsFor(entry.Entity, token)));
                }
            }
        }

        /// <summary>VenueInstance.FeedSource from the spawn mapping: "own" → null
        /// (native stream), "sim" → "sim", venue token → its id part.</summary>
        private static string? FeedSourceFor(StrategyInstance instance, string token)
        {
            var mapping = instance.VenueFeeds?.FirstOrDefault(kv =>
                kv.Key.Trim().ToLowerInvariant() == token).Value?.Trim().ToLowerInvariant();
            return mapping switch
            {
                null or "" or "own" => null,
                "sim" => "sim",
                _ when mapping.Contains(':') => mapping[(mapping.IndexOf(':') + 1)..],
                _ => mapping,
            };
        }

        /// <summary>VenueInstance.Legs from the spawn leg assignments: the
        /// comma-joined IR leg ids mapped to this venue token.</summary>
        private static string? LegsFor(StrategyInstance instance, string token)
        {
            var legs = (instance.LegVenues ?? [])
                .Where(kv => kv.Value.Trim().ToLowerInvariant() == token)
                .Select(kv => kv.Key.Trim())
                .Where(k => k.Length > 0)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();
            return legs.Count > 0 ? string.Join(',', legs) : null;
        }

        private static List<string> NormalizedVenueTokens(IEnumerable<string>? tokens) =>
            (tokens ?? [])
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();

        private static string VenueTokenOf(VenueInstance vi) =>
            vi.VenueId is { } ctrader ? $"ctrader:{ctrader}"
            : vi.AlpacaVenueId is { } alpaca ? $"alpaca:{alpaca}"
            : vi.FleetVenueId is { } fleet ? $"fleet:{fleet}"
            : $"metatrader:{vi.MetaTraderTerminalId}";

        /// <summary>Builds a venue instance from a "ctrader:{id}" / "alpaca:{id}" /
        /// "metatrader:{id}" / "fleet:{id}"
        /// token.</summary>
        private static VenueInstance SpawnVenueInstance(
            Guid strategyInstanceId, string token, string? feedSource = null, string? legs = null)
        {
            var parts = token.Split(':', 2);
            if (parts.Length != 2 || !Guid.TryParse(parts[1], out var venueId))
                throw new InvalidOperationException($"Invalid venue token “{token}”.");
            return parts[0] switch
            {
                "ctrader" => new VenueInstance
                {
                    VenueId = venueId,
                    StrategyInstanceId = strategyInstanceId,
                    Platform = VenuePlatform.CTrader,
                    Status = VenueInstanceStatus.Provisioning,
                    FeedSource = feedSource,
                    Legs = legs,
                },
                "alpaca" => new VenueInstance
                {
                    AlpacaVenueId = venueId,
                    StrategyInstanceId = strategyInstanceId,
                    Platform = VenuePlatform.Alpaca,
                    Status = VenueInstanceStatus.Provisioning,
                    FeedSource = feedSource,
                    Legs = legs,
                },
                "metatrader" => new VenueInstance
                {
                    MetaTraderTerminalId = venueId,
                    StrategyInstanceId = strategyInstanceId,
                    Platform = VenuePlatform.MetaTrader,
                    Status = VenueInstanceStatus.Provisioning,
                    FeedSource = feedSource,
                    Legs = legs,
                },
                "fleet" => new VenueInstance
                {
                    FleetVenueId = venueId,
                    StrategyInstanceId = strategyInstanceId,
                    Platform = VenuePlatform.Fleet,
                    Status = VenueInstanceStatus.Provisioning,
                    FeedSource = feedSource,
                    Legs = legs,
                },
                _ => throw new InvalidOperationException($"Unknown venue kind “{parts[0]}”."),
            };
        }

        /// <summary>
        /// Every venue instance (cTrader, MetaTrader, Alpaca) spawns an equivalent
        /// market feed instance that feeds it. Feeds die with the venue instance
        /// that owns them.
        /// </summary>
        private void CoupleFeedInstances(DateTime now)
        {
            var entries = ChangeTracker.Entries<VenueInstance>().ToList();
            foreach (var entry in entries)
            {
                if (entry.State == EntityState.Added)
                {
                    if (entry.Entity.FeedInstanceId is null)
                    {
                        // Owned feed, linked back via FeedInstance.VenueInstanceId
                        // (setting FeedInstanceId here too would create a circular
                        // insert dependency).
                        Set<FeedInstance>().Add(new FeedInstance
                        {
                            VenueInstanceId = entry.Entity.Id,
                            Platform = entry.Entity.Platform,
                        });
                    }
                }
                else if (entry.State == EntityState.Deleted)
                {
                    var feed = Set<FeedInstance>()
                        .FirstOrDefault(f => f.VenueInstanceId == entry.Entity.Id);
                    if (feed is not null) Set<FeedInstance>().Remove(feed);
                }
            }
        }
    }
}
