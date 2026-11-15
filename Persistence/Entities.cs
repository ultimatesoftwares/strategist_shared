using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;

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
        /// <summary>Venue the order executed on (ctrader/alpaca/mt5 row id);
        /// null for feed-only/unroutable orders.</summary>
        public Guid? VenueId { get; set; }

        /// <summary>Platform kind of the venue ("ctrader", "alpaca", "metatrader").</summary>
        [MaxLength(20)]
        public string VenueKind { get; set; } = "";

        [MaxLength(100)] public string OrderId { get; set; } = "";

        /// <summary>Strategy instrument the order traded.</summary>
        [MaxLength(100)]
        public string Symbol { get; set; } = "";

        [MaxLength(10)] public string Side { get; set; } = "";
        public double Quantity { get; set; }
        [Column(TypeName = "decimal(10,2)")] public decimal Price { get; set; }

        /// <summary>Realized PnL of the close this fill executed (account
        /// currency, engine close-time marks); null for opening fills,
        /// rejections and cancellations — nothing was realized.</summary>
        public double? RealizedPnl { get; set; }

        /// <summary>Sanitized broker refusal for rejected orders (e.g.
        /// "Rejected: NOT_ENOUGH_MONEY: insufficient margin"); null for
        /// fills/cancellations.</summary>
        [MaxLength(300)]
        public string? RejectReason { get; set; }

        /// <summary>The model_decisions row whose targets caused this order
        /// (quant path only); null for python-path, manual and external
        /// fills. SetNull on decision delete — but the retention purge never
        /// removes linked decisions, so every trade keeps its causing
        /// prediction.</summary>
        public long? DecisionId { get; set; }
        public ModelDecision? Decision { get; set; }

        public DateTime ExecutionTs { get; set; } = DateTime.UtcNow;
        public OrderStatus Status { get; set; } = OrderStatus.Filled;
    }


    /// <summary>A MetaTrader account: credentials captured at account setup; the
    /// fleet provisioner boots the hosted terminal container with them.</summary>
    public class MetaTraderTerminal : BaseEntity
    {
        /// <summary>Terminal platform: "metatrader5" (default) or "metatrader4".</summary>
        [MaxLength(20)]
        public string Platform { get; set; } = "metatrader5";

        public long Login { get; set; }

        /// <summary>Account password, persisted alongside login/server so the
        /// fleet can (re-)provision the terminal session at any time.</summary>
        [MaxLength(200)]
        public string Password { get; set; } = "";

        [MaxLength(100)] public string Server { get; set; } = "";

        // Optional owner; accounts stored without one stay global.
        public Guid? UserId { get; set; }
        public ApplicationUser? User { get; set; }
    }

    /// <summary>Registration of a venue hosted by the Fleet venue host
    /// (src/Fleet hub): the engine executes through the hub's REST surface and
    /// consumes its gRPC quote feed instead of a native venue adapter.</summary>
    public class FleetVenueRegistration : BaseEntity
    {
        // Optional owner; rows stored without one stay global.
        public Guid? UserId { get; set; }
        public ApplicationUser? User { get; set; }

        /// <summary>Fleet venue id — also the quote routing-key prefix
        /// (<c>{venue_id}.{canonical_instrument_id}</c>). 64 chars: self-assigned
        /// connector ids may be full UUIDs (36) or "login-hex" strings.</summary>
        [MaxLength(64)]
        public string VenueId { get; set; } = "";

        /// <summary>Base URL of the hub's public REST listener.</summary>
        [MaxLength(200)]
        public string RestUrl { get; set; } = "";

        /// <summary>Venue API key (<c>fleet_{venue_id}_{secret}</c>).</summary>
        [MaxLength(256)]
        public string ApiKey { get; set; } = "";

        public bool IsLive { get; set; }
        public bool Enabled { get; set; }
    }

    public class CTraderVenue : BaseEntity
    {
        public Guid UserId { get; set; }
        public ApplicationUser User { get; set; } = null!;

        [MaxLength(100)] public long AccountId { get; set; } = 0;
        [MaxLength(100)] public string Broker { get; set; } = "";
        [MaxLength(10)] public string Currency { get; set; } = "";
        public bool IsLive { get; set; }
        [MaxLength(512)] public string AccessToken { get; set; } = "";
        [MaxLength(512)] public string RefreshToken { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
        public bool Approved { get; set; }
        public bool Enabled { get; set; }

        /// Bumped atomically with every token change (OAuth or refresh); the
        /// engine's reconciler rotates the venue connection when it moves.
        public long CredentialsVersion { get; set; }

        /// Last refresh failure (secret-free), surfaced in the GUI; null when
        /// the last refresh succeeded. "reauth required" means the refresh token
        /// itself is dead and the human OAuth flow must be repeated.
        [MaxLength(300)]
        public string? LastRefreshError { get; set; }

        /// Marks the preferred account for market-data subscriptions.
        public bool IsMarketFeed { get; set; }
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

    /// <summary>
    /// Alpaca trading venue. Streams its own market data, so each venue instance
    /// owns an equivalent market feed instance.
    /// </summary>
    public class AlpacaVenue : BaseEntity
    {
        public Guid UserId { get; set; }
        public ApplicationUser User { get; set; } = null!;

        [MaxLength(100)] public string AccountId { get; set; } = "";
        [MaxLength(256)] public string ApiKey { get; set; } = "";
        [MaxLength(256)] public string ApiSecret { get; set; } = "";

        /// <summary>false = paper trading endpoint.</summary>
        public bool IsLive { get; set; }

        public bool Enabled { get; set; }
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


    public enum StrategyInstanceStatus
    {
        Pending,
        Running,
        Stopped,
        Failed
    }

    public class StrategyInstance : BaseEntity
    {
        public Guid UserId { get; set; }
        public ApplicationUser User { get; set; } = null!;

        /// <summary>Quant package id (sha256 hex): the immutable strategy
        /// package this instance runs through the native core runtime
        /// (quant platform M6). Required at create since the M9 legacy-strategy
        /// retirement.</summary>
        [MaxLength(64)]
        public string? PackageId { get; set; }

        /// <summary>Quant degrade policy once the runtime degrades: "hold"
        /// (default) keeps positions, "flatten" closes them (M6 design §3).</summary>
        [MaxLength(10)]
        public string? DegradePolicy { get; set; }

        /// <summary>"paper" executes REAL orders on the selected demo/paper
        /// broker accounts (spawn validation rejects live-money accounts);
        /// null/empty = live execution (M10 — the engine-simulated paper
        /// adapter rewrite is retired).</summary>
        [MaxLength(10)]
        public string? ExecutionMode { get; set; }

        /// <summary>Instrument the instance trades; routed per venue via the
        /// instrument's venue mappings.</summary>
        public Guid InstrumentId { get; set; }

        public Instrument Instrument { get; set; } = null!;

        /// <summary>Venue sessions spawned for this instance (one per selected venue).</summary>
        public ICollection<VenueInstance> VenueInstances { get; set; } = [];

        /// <summary>
        /// Desired venue set as "ctrader:{id}" / "alpaca:{id}" /
        /// "metatrader:{id}" tokens, supplied on create/update; reconciled into
        /// VenueInstances on SaveChanges. Null means "leave venues unchanged".
        /// </summary>
        [NotMapped]
        public List<string>? Venues { get; set; }

        /// <summary>Spawn-time per-venue feed mapping keyed by venue token:
        /// "own" (venue's native quote stream), "sim", or another selected
        /// venue's token to trade off that venue's stream. Applied to the
        /// spawned VenueInstances (FeedSource) on create/reconcile.</summary>
        [NotMapped]
        public Dictionary<string, string>? VenueFeeds { get; set; }

        /// <summary>Spawn-time leg assignments keyed by IR leg id → selected
        /// venue token. Applied to the spawned VenueInstances (Legs) on
        /// create/reconcile; required when the strategy IR declares legs.</summary>
        [NotMapped]
        public Dictionary<string, string>? LegVenues { get; set; }

        /// <summary>Bar-feed source driving the strategy: "sim", "questdb" or a
        /// venue id (live ticks aggregated into bars). Null/empty = sim.</summary>
        public string? Feed { get; set; }

        /// <summary>Risk sizing mode chosen at spawn: "default" (a percent of
        /// the account equity converted to lots at the current price) or
        /// "fixed" (user-specified lots). Null = the strategy's design lots.</summary>
        public string? RiskMode { get; set; }

        /// <summary>Percent of account equity risked per entry when
        /// <see cref="RiskMode"/> is "default" (1 = 1%).</summary>
        public double? RiskPercent { get; set; }

        /// <summary>Fixed lots per entry when <see cref="RiskMode"/> is "fixed".</summary>
        public double? RiskLots { get; set; }

        /// <summary>Engine-owned: updated only via NATS (db.strategy_instances).</summary>
        public StrategyInstanceStatus Status { get; set; } = StrategyInstanceStatus.Pending;
    }

    public enum VenueInstanceStatus
    {
        Provisioning,
        Running,
        Stopped,
        Failed
    }

    public enum VenuePlatform
    {
        CTrader,
        MetaTrader,
        Alpaca,
        Fleet
    }

    /// <summary>
    /// Per-strategy-instance venue session. Every strategy instance spawns its own
    /// venue instance, so instances stay independent: two strategies trading the
    /// same venue each get their own session, and closing a strategy instance only
    /// kills the venue instance it spawned.
    /// </summary>
    public class VenueInstance : BaseEntity
    {
        public Guid? VenueId { get; set; }
        public CTraderVenue? Venue { get; set; }

        public Guid? AlpacaVenueId { get; set; }
        public AlpacaVenue? AlpacaVenue { get; set; }

        public Guid? MetaTraderTerminalId { get; set; }
        public MetaTraderTerminal? MetaTraderTerminal { get; set; }

        public Guid? FleetVenueId { get; set; }
        public FleetVenueRegistration? FleetVenue { get; set; }


        public Guid StrategyInstanceId { get; set; }
        public StrategyInstance StrategyInstance { get; set; } = null!;

        public VenuePlatform Platform { get; set; } = VenuePlatform.CTrader;

        /// <summary>Spawned market feed linked back to this instance via
        /// FeedInstance.VenueInstanceId (every platform streams its own data).</summary>
        public Guid? FeedInstanceId { get; set; }

        public FeedInstance? FeedInstance { get; set; }

        /// <summary>Quote source for this venue session: null = own stream,
        /// "sim" = simulated, or a source venue id (trades off that venue's
        /// market feed — engine applies it via feed assignment).</summary>
        public string? FeedSource { get; set; }

        /// <summary>Comma-separated IR leg ids assigned to this venue at spawn
        /// (multi-leg strategies); null when the strategy declares no legs.</summary>
        public string? Legs { get; set; }

        public VenueInstanceStatus Status { get; set; } = VenueInstanceStatus.Provisioning;
    }

    public enum FeedInstanceStatus
    {
        Provisioning,
        Streaming,
        Stopped,
        Failed
    }

    /// <summary>
    /// Market feed session spawned for (and owned by) a venue instance. Dies with
    /// its owner.
    /// </summary>
    public class FeedInstance : BaseEntity
    {
        public Guid VenueInstanceId { get; set; }
        public VenueInstance VenueInstance { get; set; } = null!;

        public VenuePlatform Platform { get; set; }

        public FeedInstanceStatus Status { get; set; } = FeedInstanceStatus.Provisioning;
    }

    /// <summary>
    /// Fleet-wide venue ownership lease (table <c>venue_owner</c>, engine
    /// invariant I2: one live connection per broker account). An engine agent
    /// atomically acquires the account's row before connecting, renews it on a
    /// heartbeat, and a row whose <see cref="ExpiresAt"/> passed is free for
    /// takeover — so a crashed agent's accounts recover without double
    /// connections. Natural key: the canonical account key
    /// ("provider:account:mode").
    /// </summary>
    public class VenueOwner
    {
        [Key] [MaxLength(200)] public string AccountKey { get; set; } = "";

        /// <summary>Agent currently holding the lease.</summary>
        [MaxLength(100)]
        public string AgentId { get; set; } = "";

        public DateTime HeartbeatAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    /// <summary>
    /// Position ledger (engine-platform spec §9, invariant I1: the broker is
    /// the source of truth — this ledger is the engine's durable claim about
    /// what it OWNS so crashes never orphan or double-open a position). A row
    /// is written in state <c>Opening</c> BEFORE the order is sent (write-ahead:
    /// closes the crash window between broker fill and local record), updated
    /// to <c>Open</c> on fill, <c>Closing</c>/<c>Closed</c> on the way out.
    /// <c>Unattributed</c> rows record broker positions carrying no engine
    /// label — reported, never auto-closed.
    /// </summary>
    [Index(nameof(InstanceId), nameof(State))]
    [Index(nameof(VenueAccountKey), nameof(State))]
    public class InstancePosition
    {
        [Key] public Guid Id { get; set; }

        /// <summary>Owning strategy instance (the "strategist:{id}" label's id).</summary>
        [MaxLength(100)]
        public string InstanceId { get; set; } = "";

        [MaxLength(200)] public string Strategy { get; set; } = "";

        /// <summary>Canonical venue account key ("provider:account:mode").</summary>
        [MaxLength(200)]
        public string VenueAccountKey { get; set; } = "";

        [MaxLength(100)] public string Symbol { get; set; } = "";

        /// <summary>"buy" or "sell".</summary>
        [MaxLength(10)]
        public string Side { get; set; } = "";

        /// <summary>Canonical units (lots for FX).</summary>
        public double Quantity { get; set; }

        /// <summary>Label attached to the broker order for attribution.</summary>
        [MaxLength(150)]
        public string OrderLabel { get; set; } = "";

        /// <summary>Idempotency key attached to the submission.</summary>
        [MaxLength(64)]
        public string ClientOrderId { get; set; } = "";

        /// <summary>Broker position id once filled; 0 while Opening.</summary>
        public long BrokerPositionId { get; set; }

        public double EntryPrice { get; set; }

        /// <summary>Most favorable price seen since entry (trailing context
        /// for adoption after a restart).</summary>
        public double PeakPrice { get; set; }

        /// <summary>Risk checkpoint at entry — the broker-side disaster-stop
        /// price (0 when the venue cannot hold one).</summary>
        public double RiskCheckpoint { get; set; }

        /// <summary>Opening | Open | Closing | Closed | Unattributed.</summary>
        [MaxLength(20)]
        public string State { get; set; } = "";

        /// <summary>Secret-free transition note ("stop", "external close",
        /// "write-ahead never executed", ...).</summary>
        [MaxLength(300)]
        public string? Note { get; set; }

        /// <summary>Mark price the engine realized the close at; null when the
        /// close happened while the engine was down (no marks in hand).</summary>
        public double? ExitPrice { get; set; }

        /// <summary>Realized PnL in account currency ((exit − entry) × direction
        /// × lots × contract size); null when unknown. Daily-PnL report input.</summary>
        public double? RealizedPnl { get; set; }

        /// <summary>Agent that owns (owned) the instance.</summary>
        [MaxLength(100)]
        public string AgentId { get; set; } = "";

        public DateTime OpenedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
    }

    /// <summary>One quant-runtime step decision (table <c>model_decisions</c>,
    /// quant platform M6 / G1 report D6). Written fail-open by the engine on
    /// every closed bar of a package instance; replayed by the Server's
    /// <c>/api/model-replay</c> parity check. Purged by the engine after
    /// <c>Engine:DecisionLogRetentionDays</c> (default 14).</summary>
    [Index(nameof(InstanceId), nameof(BarTs), IsUnique = true)]
    public class ModelDecision
    {
        public long Id { get; set; }

        /// <summary>Owning strategy instance id.</summary>
        [MaxLength(100)]
        public string InstanceId { get; set; } = "";

        /// <summary>Package content hash (sha256 hex).</summary>
        [MaxLength(64)]
        public string PackageId { get; set; } = "";

        /// <summary>Closed-bar bucket start, unix seconds.</summary>
        public long BarTs { get; set; }

        /// <summary>Runtime step counter (1-based).</summary>
        public long Step { get; set; }

        /// <summary>The 6 × nLegs bar doubles fed to the runtime, as a JSON array.</summary>
        public string InputJson { get; set; } = "[]";

        /// <summary>Model forecast (null while warming or on NaN).</summary>
        public double? Forecast { get; set; }

        /// <summary>Per-leg dimensionless targets, JSON array.</summary>
        public string TargetsJson { get; set; } = "[]";

        /// <summary>Runtime status: "warmup" | "active" | "degraded".</summary>
        [MaxLength(20)]
        public string Status { get; set; } = "";

        /// <summary>True when the model was scheduled this bar.</summary>
        public bool Fired { get; set; }

        /// <summary>Native step wall time, microseconds.</summary>
        public int LatencyUs { get; set; }

        /// <summary>Order-book features at bar close (JSON object from the
        /// venue's depth plane); null when the venue publishes no depth or the
        /// book was stale.</summary>
        public string? BookJson { get; set; }

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

        /// <summary>Provider segment of the key ("ctrader", "metatrader5", "alpaca").</summary>
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
    /// Immutable quant strategy package (table <c>strategy_packages</c>, quant
    /// platform M1 / G1 report D9). A row is written only after the native core
    /// validated the uploaded .sgpkg archive; the primary key IS the sha256 of
    /// the archive bytes (lowercase hex) — a changed byte is a new package, and
    /// there is no mutable "latest" pointer. The archive bytes live in the
    /// content-addressed IPackageStore at <see cref="StoragePath"/>; this row
    /// carries the manifest (jsonb) and the promotion state.
    /// </summary>
    public class StrategyPackage
    {
        /// <summary>sha256 of the archive bytes, lowercase hex (the package id).</summary>
        [Key]
        [MaxLength(64)]
        public string Id { get; set; } = "";

        /// <summary>Manifest name ([a-z0-9][a-z0-9-_]{0,63}).</summary>
        [MaxLength(100)]
        public string Name { get; set; } = "";

        [MaxLength(128)] public string Author { get; set; } = "";
        [MaxLength(2048)] public string Description { get; set; } = "";

        /// <summary>The validated manifest.json document (jsonb).</summary>
        public string ManifestJson { get; set; } = "{}";

        /// <summary>Promotion state: "validated" (upload accepted) →
        /// "backtested" → "paper" → "live" (M6 promotion gates). Only
        /// still-"validated" packages may be deleted.</summary>
        [MaxLength(20)]
        public string Status { get; set; } = "validated";

        /// <summary>Promotion audit trail (jsonb): JSON array of
        /// {stage, at, by, note} appended on every stage advance (M6, D9).</summary>
        public string PromotionHistory { get; set; } = "[]";

        /// <summary>Archive size in bytes (upload cap: 32 MiB).</summary>
        public long SizeBytes { get; set; }

        /// <summary>Where the IPackageStore put the archive blob.</summary>
        [MaxLength(300)]
        public string StoragePath { get; set; } = "";

        /// <summary>strategist-core CORE_VERSION that validated the package.</summary>
        [MaxLength(50)]
        public string CoreVersion { get; set; } = "";

        public DateTime CreatedAtUtc { get; set; }
    }

    /// <summary>
    /// Persistent package-backtest result (table <c>backtest_results</c>, quant
    /// platform M4 / G1 report D9 — keyed by the bus job id directly per the M4
    /// design freeze). The row is
    /// created on submission (Status "queued") and the PackageBacktestJobMessage
    /// is sent to the slave's durable "backtest.jobs" queue at the same time; the
    /// slave's PackageBacktestStateMessage frames are mirrored back onto the row.
    /// Not keyed by <see cref="BaseEntity"/> (string pk), so writers stamp
    /// CreatedAt/UpdatedAt themselves.
    /// </summary>
    public class BacktestResult
    {
        /// <summary>Job id the bus and the GUI key on ("pbt-{hex}").</summary>
        [Key]
        [MaxLength(200)]
        public string Id { get; set; } = "";

        /// <summary>Content hash of the replayed package (strategy_packages.Id).</summary>
        [MaxLength(64)]
        public string PackageId { get; set; } = "";

        /// <summary>"queued" | "running" | "completed" | "failed" | "cancelled".</summary>
        [MaxLength(20)]
        public string Status { get; set; } = "queued";

        /// <summary>Bars replayed so far (final count on terminal frames).</summary>
        public long Bars { get; set; }

        /// <summary>Cooperative cancel flag: set via the API, relayed to the
        /// slave over the bus-wide cancel publish.</summary>
        public bool CancelRequested { get; set; }

        [MaxLength(1024)] public string? Error { get; set; }

        // ── the requested replay window/config, as sent on the job message ──
        /// <summary>Bar timeframe; empty = "use the manifest's timeframe".</summary>
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

        /// <summary>Slave-reported start of the replay (first frame).</summary>
        public DateTime? StartedAt { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
    }

    /// <summary>
    /// Persistent market-data provisioning job (table <c>provision_jobs</c> —
    /// the provisioning counterpart of <see cref="BacktestResult"/>): the row
    /// is created when the job is queued to the fleet hub's durable data-jobs
    /// queue and the hub's ProvisionStateMessage frames are mirrored back onto
    /// it, so jobs survive server restarts. Finished rows prune from the
    /// listing an hour after <see cref="StartedAt"/>. Not keyed by
    /// <see cref="BaseEntity"/> (the id rides the bus), so writers stamp the
    /// timestamps themselves.
    /// </summary>
    public class ProvisionJob
    {
        /// <summary>Job id the bus and the GUI key on.</summary>
        [Key]
        public Guid Id { get; set; }

        /// <summary>Download source ("ctrader", "alpaca", "metatrader",
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

        /// <summary>StrategyPackage.Id (content hash, string pk — no FK so links
        /// survive registry re-uploads; consumers resolve lazily).</summary>
        [MaxLength(200)]
        public string PackageId { get; set; } = "";

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

// ---------------------------------------------------------------------------
// Fleet venue host (src/Fleet): the hub's provisioning registry. The hub is
// the only writer of the fleet_* tables; the Server owns the migrations like
// every other table in this schema. Runtime-only state (connected flags,
// heartbeats, key last-used) stays in the hub's in-memory RuntimeState.
// ---------------------------------------------------------------------------

    /// <summary>A venue connection managed by the fleet hub, one per client
    /// (table <c>fleet_venues</c>): an MT4/MT5 terminal container, or an
    /// in-process alpaca/ctrader API session. Rows are created by the admin API
    /// (provisioned venues) or by the hub itself when an unknown connector says
    /// Hello (self-registered MT terminals: the EA self-assigns a stable id — a
    /// full UUID or "login-hex" — and the hub trusts the internal listener's
    /// network as the boundary).</summary>
    public class FleetVenue
    {
        // 64 chars: admin-provisioned ids are "venue_<hex>" (19; historical rows
        // may carry "term_<hex>"), self-assigned connector ids are UUIDs (36) or
        // "<login>-<hex>" strings.
        [MaxLength(64)] public string Id { get; set; } = NewVenueId();
        [MaxLength(128)] public string ClientId { get; set; } = "";

        /// <summary>"mt5" | "mt4" | "alpaca" | "ctrader" (Fleet.Hub.Data.Platforms).</summary>
        [MaxLength(16)]
        public string Platform { get; set; } = "mt5";

        public long AccountLogin { get; set; }

        /// <summary>Optional account username for platforms that log in with a
        /// username (MT platforms use AccountLogin).</summary>
        [MaxLength(128)]
        public string AccountUsername { get; set; } = "";

        [MaxLength(128)] public string BrokerServer { get; set; } = "";

        /// <summary>
        /// Broker's corporate name (e.g. "IC Markets"), optional. On a fresh
        /// terminal the text server name alone cannot be resolved; writing
        /// Broker= before Server= in the start config forces a first-run sync of
        /// the broker's server list from the MetaQuotes directory.
        /// </summary>
        [MaxLength(128)]
        public string BrokerName { get; set; } = "";

        /// <summary>Presented by the venue's terminal connector in its Hello
        /// message (MT platforms only).</summary>
        [MaxLength(64)]
        public string SidecarToken { get; set; } = NewSidecarToken();

        /// <summary>
        /// Platform-specific credentials for in-process venues (ctrader / alpaca)
        /// as a JSON object — those venues have no terminal
        /// container to hold their secrets. Container platforms (mt5/mt4)
        /// never store the password here: it passes through to the provisioner
        /// as transient container env. Server-mirrored MT rows (client id
        /// "mt-{guid}", Hub.Venues.MetaTrader.MetaTraderAccountSync) keep a
        /// SHA-256 fingerprint of the password for change detection;
        /// other mt5/mt4 rows keep this null.
        /// </summary>
        public string? CredentialsJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public List<FleetApiKey> ApiKeys { get; set; } = [];

        public static string NewVenueId() => "venue_" + RandomHex(8);
        public static string NewSidecarToken() => RandomHex(32);

        public static string RandomHex(int bytes) =>
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(bytes));
    }

    /// <summary>
    /// A client credential for the fleet's public API (table <c>fleet_api_keys</c>).
    /// Only the SHA-256 of the secret is kept; the full key
    /// ("mtf_&lt;key_id&gt;_&lt;secret&gt;") is returned exactly once at creation and
    /// never stored.
    /// </summary>
    public class FleetApiKey
    {
        [MaxLength(16)] public string KeyId { get; set; } = "";
        [MaxLength(64)] public string VenueId { get; set; } = "";
        public FleetVenue Venue { get; set; } = null!;
        [MaxLength(128)] public string Name { get; set; } = "";

        /// <summary>Hex SHA-256 of the key secret.</summary>
        [MaxLength(64)]
        public string SecretHash { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Maps one canonical strategist instrument to its symbol at one fleet venue
    /// (table <c>fleet_instrument_mappings</c>). A venue subscribes to exactly its
    /// enabled rows; quotes publish under the canonical id (routing key
    /// "{venue}.{canonical}"), so consumers never see venue-specific symbols.
    /// </summary>
    public class FleetInstrumentMapping
    {
        public long Id { get; set; }
        [MaxLength(64)] public string VenueId { get; set; } = "";
        public FleetVenue Venue { get; set; } = null!;

        /// <summary>Canonical instrument id from the instruments table
        /// (e.g. "EURUSD").</summary>
        [MaxLength(64)]
        public string CanonicalInstrumentId { get; set; } = "";

        /// <summary>The instrument's symbol at this venue (e.g. "EURUSD.m").</summary>
        [MaxLength(64)]
        public string VenueSymbol { get; set; } = "";

        public bool Enabled { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// One symbol of a fleet venue's instrument catalog (table
    /// <c>fleet_venue_instruments</c>). The hub downloads the venue's full
    /// symbol catalog on every connect (MT: Market Watch via the connector;
    /// in-process venues: the platform API) and replaces the venue's rows, so
    /// the instrument-mapping UI can offer the venue's real symbols instead of
    /// free-text guessing.
    /// </summary>
    public class FleetVenueInstrument
    {
        public long Id { get; set; }
        [MaxLength(64)] public string VenueId { get; set; } = "";
        public FleetVenue Venue { get; set; } = null!;

        /// <summary>The symbol as the venue spells it (e.g. "EURUSD.m").</summary>
        [MaxLength(64)]
        public string Symbol { get; set; } = "";

        [MaxLength(256)] public string Description { get; set; } = "";
        public int Digits { get; set; }
        public double ContractSize { get; set; }
        public double VolumeMin { get; set; }
        public double VolumeMax { get; set; }
        public double VolumeStep { get; set; }
        [MaxLength(16)] public string CurrencyBase { get; set; } = "";
        [MaxLength(16)] public string CurrencyProfit { get; set; } = "";
        [MaxLength(16)] public string CurrencyMargin { get; set; } = "";
        public bool TradeAllowed { get; set; }

        /// <summary>Last catalog sync that saw this symbol.</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
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
        /// <summary>Stable terminal_id (GUID hex); the EA presents the same id
        /// when it dials the engine's TerminalService.</summary>
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
        public bool AutoStart { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}