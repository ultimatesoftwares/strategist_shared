using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Server.Models
{

    public class ApplicationUser : IdentityUser<Guid>
    {
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>Relative path of the uploaded profile picture under the media root.</summary>
        public string? AvatarPath { get; set; }

        /// <summary>Admin-created accounts start with a temporary password; the
        /// client blocks entry until the first successful password change.</summary>
        public bool MustChangePassword { get; set; }

        public ICollection<Address> Addresses { get; set; } = [];
        public UserInformation? Information { get; set; }
    }

    public abstract class BaseEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class Address : BaseEntity
    {
        public Guid UserId { get; set; }
        public ApplicationUser User { get; set; } = null!;

        [MaxLength(100)] public string Street { get; set; } = "";
        [MaxLength(50)] public string City { get; set; } = "";
        [MaxLength(50)] public string State { get; set; } = "";
        [MaxLength(10)] public string ZipCode { get; set; } = "";
        [MaxLength(60)] public string Country { get; set; } = "";
    }

    public enum Gender
    {
        Male,
        Female
    }

    public class UserInformation : BaseEntity
    {
        public Guid UserId { get; set; }
        public ApplicationUser User { get; set; } = null!;

        public Gender Gender { get; set; }
        public DateOnly DateOfBirth { get; set; }
        [MaxLength(20)] public string Phone { get; set; } = "";

        [MaxLength(100)] public string FirstName { get; set; } = "";
        [MaxLength(100)] public string LastName { get; set; } = "";
    }

    public enum OrderStatus
    {
        Filled,
        Pending,
        Cancelled,
        Rejected
    }

    public class Order : BaseEntity
    {
        /// <summary>Venue the order executed on (ctrader/mt5 row id);
        /// null for feed-only/unroutable orders.</summary>
        public Guid? VenueId { get; set; }

        /// <summary>Platform kind of the venue ("ctrader", "metatrader").</summary>
        [MaxLength(20)]
        public string VenueKind { get; set; } = "";

        [MaxLength(100)] public string OrderId { get; set; } = "";

        /// <summary>Broker-issued identifiers, when supplied by the execution adapter.</summary>
        [MaxLength(32)] public string? BrokerOrderTicket { get; set; }
        [MaxLength(32)] public string? BrokerPositionTicket { get; set; }
        [MaxLength(256)] public string? BrokerAccountKey { get; set; }

        /// <summary>Strategy instrument the order traded.</summary>
        [MaxLength(100)]
        public string Symbol { get; set; } = "";

        [MaxLength(10)] public string Side { get; set; } = "";
        /// <summary>True only for a confirmed position-closing execution; null when unknown.</summary>
        public bool? IsPositionClose { get; set; }
        public double Quantity { get; set; }
        [Column(TypeName = "decimal(10,2)")] public decimal Price { get; set; }

        /// <summary>Realized PnL; legacy values may be unverified engine estimates.
        /// Consult RealizedPnlSource before presenting this as broker data.</summary>
        public double? RealizedPnl { get; set; }
        [MaxLength(32)] public string? RealizedPnlSource { get; set; }

        /// <summary>Sanitized broker refusal for rejected orders (e.g.
        /// "Rejected: NOT_ENOUGH_MONEY: insufficient margin"); null for
        /// fills/cancellations.</summary>
        [MaxLength(300)]
        public string? RejectReason { get; set; }

        /// <summary>The model_decisions row whose signal caused this order
        /// (strategy path only); null for manual and external fills.
        /// SetNull on decision delete so the order row survives.</summary>
        public Guid? DecisionId { get; set; }
        public ModelDecision? Decision { get; set; }

        public DateTime ExecutionTs { get; set; } = DateTime.UtcNow;
        public OrderStatus Status { get; set; } = OrderStatus.Filled;
    }

    public class CTraderVenue : BaseEntity
    {
        public Guid UserId { get; set; }
        public ApplicationUser User { get; set; } = null!;

        [MaxLength(100)] public long AccountId { get; set; } = 0;
        [MaxLength(100)] public string Broker { get; set; } = "";
        [MaxLength(10)] public string Currency { get; set; } = "";
        public bool IsLive { get; set; }
        public bool Approved { get; set; }
        public bool Enabled { get; set; }

        /// <summary>cTrader FIX API credentials (per trader login, from the
        /// terminal's Settings &gt; FIX API dialog) — the venue's only
        /// credentials (the Open API/OAuth onboarding is retired): they drive
        /// the receive-only FIX fill-capture session. An empty host or
        /// password means FIX capture is not configured.</summary>
        [MaxLength(200)] public string FixHost { get; set; } = "";

        /// <summary>FIX TRADE port from the same dialog (SSL and plain differ).</summary>
        public int FixPort { get; set; }

        public bool FixSsl { get; set; } = true;

        /// <summary>SenderCompID (tag 49): "&lt;env&gt;.&lt;brokerUid&gt;.&lt;login&gt;".</summary>
        [MaxLength(200)] public string FixSenderCompId { get; set; } = "";

        /// <summary>Numeric trader login (tag 553).</summary>
        [MaxLength(50)] public string FixLogin { get; set; } = "";

        [MaxLength(256)] public string FixPassword { get; set; } = "";

        /// Bumped atomically with every FIX credential change. The engine's
        /// fill capture (re)starts a session only when this moves — re-saving
        /// credentials is the operator's acknowledgment that journaled fills
        /// were reconciled after a capture fault.
        public long FixCredentialsVersion { get; set; }
    }

    public enum AssetClass
    {
        Forex,
        Metal,
        Bonds,
        Crypto,
        Equities,
        Indices,
        Commodities,
        Futures,
        Options
    }

    /// <summary>Canonical instrument: venue-agnostic reference data. Where it
    /// trades (and under what venue-native symbol/id) lives on
    /// <see cref="InstrumentCatalogEntry"/> rows whose <c>mapped_to</c> points
    /// here.</summary>
    public class Instrument : BaseEntity
    {
        [MaxLength(100)] public string Symbol { get; set; } = "";
        public AssetClass AssetClass { get; set; }
        [MaxLength(10)] public string? BaseCurrency { get; set; }
        [MaxLength(10)] public string? QuoteCurrency { get; set; }
        public int Digits { get; set; }
        [MaxLength(256)] public string Description { get; set; } = "";
        public bool IsTradable { get; set; } = true;
    }

    public sealed class InstrumentArtwork
    {
        public Guid InstrumentId { get; set; }
        public byte[]? Icon { get; set; }
        public byte[]? BaseIcon { get; set; }
        public byte[]? QuoteIcon { get; set; }
    }


    /// <summary>One strategy decision that emitted orders (table
    /// <c>model_decisions</c>). Written by the bot's OrderLog from the Rust
    /// engine's signal snapshot — only for evaluations that placed ≥ 1 order
    /// (strategies evaluate per book update; journaling every evaluation
    /// would be thousands of rows/sec). <c>orders.DecisionId</c> points back
    /// at the row that caused each order.</summary>
    [Index(nameof(Account), nameof(EventTs))]
    public class ModelDecision
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Trading account identity (e.g. MT login, cTrader account id).</summary>
        [MaxLength(100)]
        public string Account { get; set; } = "";

        /// <summary>Venue family: "mt4" | "mt5" | "ctrader" | "sim".</summary>
        [MaxLength(20)]
        public string VenueKind { get; set; } = "";

        /// <summary>Venue row id where known (ctrader); null for fleet terminals.</summary>
        public Guid? VenueId { get; set; }

        [MaxLength(100)]
        public string Symbol { get; set; } = "";

        /// <summary>Strategy descriptor, e.g. "ml:models/x.zip" (historical rows may
        /// carry the retired "imbalance_quoter").</summary>
        [MaxLength(200)]
        public string Strategy { get; set; } = "";

        /// <summary>Exchange timestamp of the book update that triggered the decision.</summary>
        public DateTime EventTs { get; set; }

        /// <summary>Dimensionless strategy signal (null when none was computed,
        /// e.g. warm-up or degraded inference).</summary>
        public double? Forecast { get; set; }

        /// <summary>Signal status: "warmup" | "active" | "degraded".</summary>
        [MaxLength(20)]
        public string Status { get; set; } = "";

        /// <summary>Top-of-book + position snapshot the strategy acted on, JSON object.</summary>
        public string BookJson { get; set; } = "{}";

        public DateTime CreatedAt { get; set; }
    }

    /// <summary>One venue-native instrument listing from a broker catalog sync
    /// (table <c>instrument_catalog</c>). Rows are upserted by the engine's
    /// catalog sync (adapter GetCatalogAsync on venue Ready + schedule) AND by
    /// the server's onboarding-time catalog sync (venue created/connected), keyed
    /// by (AccountKey, VenueSymbol); <see cref="CatalogVersion"/> stamps the sync
    /// batch. <see cref="MappedTo"/> links a row to the registered instrument a
    /// user mapped it to — the single source of truth for instrument→venue
    /// symbol resolution (the retired <c>instrument_mappings</c> table's job),
    /// making the mapping pipeline DB-stable (no broker round-trip per
    /// lookup).</summary>
    [Index(nameof(AccountKey), nameof(VenueSymbol), IsUnique = true)]
    [Index(nameof(AccountKey), nameof(SyncedAt))]
    public class InstrumentCatalogEntry
    {
        public Guid Id { get; set; }

        /// <summary>Canonical venue account key ("ctrader:1234:demo").</summary>
        [MaxLength(200)]
        public string AccountKey { get; set; } = "";

        /// <summary>Provider segment of the key ("ctrader", "metatrader5").</summary>
        [MaxLength(30)]
        public string Provider { get; set; } = "";

        [MaxLength(100)] public string VenueSymbol { get; set; } = "";

        /// <summary>Venue-native numeric id (cTrader symbolId; 0 when unused).</summary>
        public long VenueSymbolId { get; set; }

        /// <summary>Human-readable listing description from the venue catalog
        /// ("Euro vs US Dollar", "Apple Inc. Common Stock"); empty when the
        /// venue serves none.</summary>
        [MaxLength(300)]
        public string Description { get; set; } = "";

        /// <summary>Venue-reported asset class token ("us_equity", "crypto",
        /// "perp", …); empty when the venue serves none.</summary>
        [MaxLength(30)]
        public string AssetClass { get; set; } = "";

        public double TickSize { get; set; }

        /// <summary>Canonical-to-venue quantity conversion factor.</summary>
        public double ContractSize { get; set; } = 1;

        public double MinQuantity { get; set; }
        public double MaxQuantity { get; set; }
        public double QuantityStep { get; set; }
        [MaxLength(20)] public string Currency { get; set; } = "";
        public bool Tradable { get; set; } = true;

        /// <summary>The registered instrument this venue symbol was mapped to in
        /// the Map Instruments flow (column <c>mapped_to</c>); null until a user
        /// maps it. SetNull on instrument deletion.</summary>
        public Guid? MappedTo { get; set; }

        public Instrument? MappedToInstrument { get; set; }

        /// <summary>Sync-batch stamp (UTC ticks of the sync) shared by every row
        /// written in one pass.</summary>
        public long CatalogVersion { get; set; }

        public DateTime SyncedAt { get; set; }
    }

    /// <summary>
    /// Registered ML.NET strategy model (table <c>strategy_models</c>): the
    /// artifact a strategy project trains and uploads (or a pre-built
    /// model.zip uploaded directly). The primary key IS the sha256 of the model bytes
    /// (lowercase hex) — re-uploading the same bytes is idempotent, a changed
    /// byte is a new model, and there is no mutable "latest" pointer. The
    /// bytes live under the server's models dir at <see cref="StoragePath"/>;
    /// the row is the catalog entry.
    /// </summary>
    public class StrategyModel
    {
        /// <summary>sha256 of the model bytes, lowercase hex (the model id).</summary>
        [Key]
        [MaxLength(64)]
        public string Id { get; set; } = "";

        /// <summary>Display name ([a-z0-9][a-z0-9-_]{0,63}); defaults to the
        /// uploaded filename's stem.</summary>
        [MaxLength(100)]
        public string Name { get; set; } = "";

        [MaxLength(2048)] public string Description { get; set; } = "";

        /// <summary>Account name of the uploader.</summary>
        [MaxLength(256)]
        public string UploadedBy { get; set; } = "";

        /// <summary>Model size in bytes.</summary>
        public long SizeBytes { get; set; }

        /// <summary>Where the model blob lives under the models dir.</summary>
        [MaxLength(300)]
        public string StoragePath { get; set; } = "";

        public DateTime CreatedAtUtc { get; set; }

        /// <summary>Minimum account balance (USD) a trading account needs
        /// before this model may be deployed on it; null = no minimum.</summary>
        public decimal? MinBalanceUsd { get; set; }

        /// <summary>Comma-separated venue providers (ctrader, mt4,
        /// mt5) the model may run on; empty = every venue.</summary>
        [MaxLength(200)]
        public string Venues { get; set; } = "";

        /// <summary>Position accounting the model needs: all | netting | hedging.</summary>
        [MaxLength(16)]
        public string AccountType { get; set; } = "all";

        /// <summary>Master switch: false stops the model on every account it
        /// is deployed to (they run the null strategy) while keeping the
        /// per-account deployments for when it is turned back on.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Order size in engine lots for accounts running this
        /// model (each venue converts engine lots to its own units); null =
        /// the bot's fleet-default order size.</summary>
        public long? OrderQty { get; set; }

        /// <summary>Comma-separated registered instrument IDs; null means all
        /// mapped instruments, while an empty string disables every instrument.</summary>
        public string? InstrumentIds { get; set; }
    }

    /// <summary>
    /// Per-trading-account model deployment (table <c>strategy_deployments</c>):
    /// the bot runs <see cref="ModelId"/> on the account named
    /// <see cref="AccountKey"/> instead of the fleet-default strategy. One
    /// model per account; no row = fleet default.
    /// </summary>
    public class StrategyDeployment
    {
        /// <summary>Bot account name, e.g. <c>ctrader:123:live</c>.</summary>
        [Key]
        [MaxLength(300)]
        public string AccountKey { get; set; } = "";

        [MaxLength(64)]
        public string ModelId { get; set; } = "";

        [MaxLength(256)]
        public string UpdatedBy { get; set; } = "";

        public DateTime UpdatedAtUtc { get; set; }
    }

    /// <summary>
    /// Broker-reported trading-account facts (table <c>trading_account_profiles</c>),
    /// keyed by the bot account name. Written when the account connects:
    /// MT4/MT5 from the connector hello's margin mode (cTrader has no
    /// broker-reported source since the Open API onboarding was retired).
    /// Model deployment constraints read it.
    /// </summary>
    public class TradingAccountProfile
    {
        /// <summary>Bot account name, e.g. <c>ctrader:123:live</c> or <c>mt5:42:Broker-Demo</c>.</summary>
        [Key]
        [MaxLength(300)]
        public string AccountKey { get; set; } = "";

        /// <summary>Venue provider: ctrader, mt4, mt5, …</summary>
        [MaxLength(32)]
        public string Provider { get; set; } = "";

        /// <summary><c>netting</c>, <c>hedging</c> or <c>spread_betting</c>; null when unknown.</summary>
        [MaxLength(32)]
        public string? AccountType { get; set; }

        /// <summary>Where the value came from, e.g. <c>ctrader-onboarding</c>, <c>ctrader-connect</c>, <c>mt5-hello</c>.</summary>
        [MaxLength(64)]
        public string Source { get; set; } = "";

        public DateTime UpdatedAtUtc { get; set; }
    }

    /// <summary>
    /// Persistent backtest result (table <c>backtest_results</c>): the research
    /// engine's job mirror — rows are upserted by its BacktestResultMirror as
    /// jobs progress so the dashboard/server can list engine runs. The disk
    /// journal stays the engine's own source of truth for restart replay.
    /// Not keyed by <see cref="BaseEntity"/> (string pk), so writers stamp
    /// CreatedAt/UpdatedAt themselves.
    /// </summary>
    public class BacktestResult
    {
        /// <summary>Job id the GUI keys on.</summary>
        [Key]
        [MaxLength(200)]
        public string Id { get; set; } = "";

        /// <summary>"queued" | "running" | "completed" | "failed" | "cancelled".</summary>
        [MaxLength(20)]
        public string Status { get; set; } = "queued";

        /// <summary>Bars replayed so far (final count on terminal frames).</summary>
        public long Bars { get; set; }

        /// <summary>Cooperative cancel flag: set via the API, relayed to the
        /// research engine.</summary>
        public bool CancelRequested { get; set; }

        [MaxLength(1024)] public string? Error { get; set; }

        // ── the requested replay window/config, as sent on the job ──
        /// <summary>Bar timeframe; empty = the job's default.</summary>
        [MaxLength(10)]
        public string Timeframe { get; set; } = "";

        /// <summary>Replay window, unix seconds, [FromTs, ToTs).</summary>
        public long FromTs { get; set; }

        public long ToTs { get; set; }

        /// <summary>Bars at/after this instant are scored OOS; 0 = no split.</summary>
        public long OosFromTs { get; set; }

        /// <summary>Hard cap on bars replayed; 0 = no cap.</summary>
        public long MaxBars { get; set; }

        /// <summary>Fill model: "" (core default) | "next_open" | "current_close".</summary>
        [MaxLength(20)]
        public string Fill { get; set; } = "";

        /// <summary>The report's "summary" object (jsonb), extracted on the
        /// terminal frame so lists stay cheap.</summary>
        public string? SummaryJson { get; set; }

        /// <summary>The core's full capped report (jsonb, backtest-report/1).</summary>
        public string? ReportJson { get; set; }

        /// <summary>Engine-reported start of the replay (first frame).</summary>
        public DateTime? StartedAt { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
    }

    /// <summary>
    /// Persistent market-data provisioning job (table <c>provision_jobs</c> —
    /// the provisioning counterpart of <see cref="BacktestResult"/>): the row
    /// is created when the fetch job is queued and the research engine's
    /// progress frames are mirrored back onto it, so jobs survive restarts.
    /// Finished rows prune from the listing an hour after
    /// <see cref="StartedAt"/>. Not keyed by <see cref="BaseEntity"/>
    /// (string job id), so writers stamp the timestamps themselves.
    /// </summary>
    public class ProvisionJob
    {
        /// <summary>Job id the bus and the GUI key on.</summary>
        [Key]
        public Guid Id { get; set; }

        /// <summary>Download source ("ctrader", "metatrader",
        /// "databento").</summary>
        [MaxLength(40)]
        public string Source { get; set; } = "";

        /// <summary>Venue registration row id (the sources listing GUID).</summary>
        public Guid VenueId { get; set; }

        /// <summary>QuestDB instrument tag, e.g. "EURUSD".</summary>
        [MaxLength(100)]
        public string Symbol { get; set; } = "";

        /// <summary>Numeric cTrader symbol id, when the caller resolved one.</summary>
        public long? SymbolId { get; set; }

        /// <summary>Bar period ("M1", "H1", …) or "tick".</summary>
        [MaxLength(10)]
        public string Timeframe { get; set; } = "";

        /// <summary>Requested range — retained so a failed job can be retried
        /// with the original window.</summary>
        public DateTime FromUtc { get; set; }

        public DateTime ToUtc { get; set; }

        /// <summary>Server-side "queued", then the hub's wire states:
        /// "running" | "completed" | "failed".</summary>
        [MaxLength(20)]
        public string Status { get; set; } = "queued";

        [MaxLength(1024)] public string? Error { get; set; }

        /// <summary>Fraction complete, 0..1 (hub chunk progress).</summary>
        public double Progress { get; set; }

        /// <summary>QuestDB rows upserted so far (final count when finished).</summary>
        public int WrittenRows { get; set; }

        /// <summary>Queue time; reset on retry (drives listing order and the
        /// one-hour prune of finished rows).</summary>
        public DateTime StartedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }

// ── Projects (collaboration workspaces) ───────────────────────────────

    public enum ProjectStatus
    {
        Draft,
        Active,
        Archived
    }

    /// <summary>Per-project collaborator role. Grants map in
    /// Server.Services.ProjectRoleGrants (single enforcement point):
    /// Owner → everything; AssetManager → edit/approve/allocate;
    /// PortfolioManager → portfolio composition (strategy links) + approval;
    /// QuantResearcher → research uploads (notebooks, documents);
    /// QuantDeveloper → strategy links (builder packages);
    /// RiskManager → risk policies.</summary>
    public enum ProjectRole
    {
        Owner,
        AssetManager,
        PortfolioManager,
        QuantResearcher,
        QuantDeveloper,
        RiskManager
    }

    /// <summary>
    /// Collaboration workspace (table <c>projects</c>): asset managers, quant
    /// researchers, quant developers and risk managers working one mandate.
    /// Members carry a per-project <see cref="ProjectRole"/>; everything the
    /// project owns (documents, strategy links, risk policies, activity) hangs
    /// off the project row and dies with it.
    /// </summary>
    public class Project : BaseEntity
    {
        [MaxLength(200)] public string Name { get; set; } = "";
        [MaxLength(4000)] public string Description { get; set; } = "";
        public ProjectStatus Status { get; set; } = ProjectStatus.Draft;

        /// <summary>The creating user; also always present in Members as Owner.
        /// Null after the owner account was deleted (the project survives).</summary>
        public Guid? OwnerId { get; set; }

        public ApplicationUser? Owner { get; set; }

        public ICollection<ProjectMember> Members { get; set; } = [];
    }

    /// <summary>One collaborator on a project (table <c>project_members</c>,
    /// unique per project + user).</summary>
    public class ProjectMember : BaseEntity
    {
        public Guid ProjectId { get; set; }
        public Project? Project { get; set; }

        public Guid UserId { get; set; }
        public ApplicationUser? User { get; set; }

        public ProjectRole Role { get; set; } = ProjectRole.QuantResearcher;
    }

    /// <summary>
    /// Research artifact uploaded into a project (table <c>project_documents</c>):
    /// papers/notes ("document") or Jupyter notebooks ("notebook"). The bytes live
    /// on disk under ServerConfig.ProjectsDir; the row is the catalog entry.
    /// </summary>
    public class ProjectDocument : BaseEntity
    {
        public Guid ProjectId { get; set; }
        public Project? Project { get; set; }

        /// <summary>Null after the uploader account was deleted.</summary>
        public Guid? UploadedById { get; set; }

        public ApplicationUser? UploadedBy { get; set; }

        [MaxLength(260)] public string FileName { get; set; } = "";
        [MaxLength(100)] public string ContentType { get; set; } = "";
        public long SizeBytes { get; set; }

        /// <summary>"document" | "notebook" (.ipynb uploads).</summary>
        [MaxLength(20)]
        public string Kind { get; set; } = "document";

        /// <summary>Relative storage path under the projects media root.</summary>
        [MaxLength(500)]
        public string StoragePath { get; set; } = "";

        [MaxLength(2000)] public string Notes { get; set; } = "";
    }

    /// <summary>
    /// Versioned risk definition a risk manager authors for a project (table
    /// <c>project_risk_policies</c>). Rules are a queryable jsonb document
    /// (limits like max drawdown, exposure caps, per-instrument caps); saving a
    /// new revision bumps <see cref="Version"/> — at most one row per project is
    /// <see cref="Active"/>.
    /// </summary>
    public class ProjectRiskPolicy : BaseEntity
    {
        public Guid ProjectId { get; set; }
        public Project? Project { get; set; }

        /// <summary>Null after the author account was deleted.</summary>
        public Guid? AuthorId { get; set; }

        public ApplicationUser? Author { get; set; }

        [MaxLength(200)] public string Name { get; set; } = "";

        /// <summary>Risk rules (jsonb), e.g. {"maxDrawdownPct":10,"maxExposurePct":50}.</summary>
        public string RulesJson { get; set; } = "{}";

        public int Version { get; set; } = 1;
        public bool Active { get; set; }
    }

    /// <summary>
    /// A builder strategy package attached to a project (table
    /// <c>project_strategy_links</c>) by a quant developer. Stage tracks the
    /// project-side lifecycle (research → development → approved); the asset
    /// manager's approval gate flips it to "approved".
    /// </summary>
    public class ProjectStrategyLink : BaseEntity
    {
        public Guid ProjectId { get; set; }
        public Project? Project { get; set; }

        /// <summary>Null after the linking account was deleted.</summary>
        public Guid? LinkedById { get; set; }

        public ApplicationUser? LinkedBy { get; set; }

        /// <summary>StrategyModel.Id (content hash, string pk — no FK so links
        /// survive registry re-uploads; consumers resolve lazily).</summary>
        [MaxLength(200)]
        public string ModelId { get; set; } = "";

        /// <summary>"research" | "development" | "approved".</summary>
        [MaxLength(20)]
        public string Stage { get; set; } = "development";

        [MaxLength(2000)] public string Notes { get; set; } = "";
    }

    /// <summary>Project audit trail (table <c>project_activity</c>): who did what
    /// (member added, document uploaded, policy activated, …), append-only.</summary>
    public class ProjectActivity : BaseEntity
    {
        public Guid ProjectId { get; set; }
        public Project? Project { get; set; }

        /// <summary>Acting user; null when the actor account was deleted.</summary>
        public Guid? UserId { get; set; }

        public ApplicationUser? User { get; set; }

        /// <summary>Machine-readable action key, e.g. "member.added".</summary>
        [MaxLength(100)]
        public string Action { get; set; } = "";

        [MaxLength(2000)] public string Detail { get; set; } = "";
    }

    /// <summary>
    /// fleetd's terminal registry (table <c>fleet_terminals</c>): one row per
    /// MetaTrader terminal managed by a fleetd host — identity plus how to
    /// launch its process (wine + terminal64.exe in a dedicated home copied
    /// from the platform template). Replaces fleetd's local
    /// <c>terminals.json</c> when a database is configured. Rows are scoped by
    /// <see cref="HostId"/> so several fleetd VMs can share the schema.
    /// </summary>
    public class FleetTerminal
    {
        /// <summary>Stable terminal_id (GUID hex); the terminal bridge logs on
        /// to the engine's FIX acceptor with this id as its SenderCompID.</summary>
        [MaxLength(64)]
        public string Id { get; set; } = "";

        /// <summary>The fleetd host owning this terminal (Fleet:HostId,
        /// default machine name). Each host loads only its own rows.</summary>
        [MaxLength(128)]
        public string HostId { get; set; } = "";

        [MaxLength(128)] public string Name { get; set; } = "";

        /// <summary>"mt4" | "mt5"; selects the provisioning template.</summary>
        [MaxLength(8)]
        public string Platform { get; set; } = "mt5";

        /// <summary>Provisioned terminal home on the host (empty for
        /// registration-only terminals that run elsewhere).</summary>
        [MaxLength(512)]
        public string HomeDir { get; set; } = "";

        /// <summary>Executable to launch, e.g. "/usr/bin/wine". Empty = unmanaged.</summary>
        [MaxLength(512)]
        public string Command { get; set; } = "";

        /// <summary>Launch arguments (JSON array of strings, jsonb).</summary>
        public string ArgsJson { get; set; } = "[]";

        [MaxLength(512)] public string WorkingDir { get; set; } = "";

        /// <summary>Extra process environment (JSON object, jsonb).</summary>
        public string EnvironmentJson { get; set; } = "{}";

        /// <summary>Start the process automatically when fleetd boots.</summary>
        public bool AutoStart { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}