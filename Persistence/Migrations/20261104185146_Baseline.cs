using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "backtest_results",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PackageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Bars = table.Column<long>(type: "bigint", nullable: false),
                    CancelRequested = table.Column<bool>(type: "boolean", nullable: false),
                    Error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Timeframe = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    FromTs = table.Column<long>(type: "bigint", nullable: false),
                    ToTs = table.Column<long>(type: "bigint", nullable: false),
                    OosFromTs = table.Column<long>(type: "bigint", nullable: false),
                    MaxBars = table.Column<long>(type: "bigint", nullable: false),
                    Fill = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SummaryJson = table.Column<string>(type: "jsonb", nullable: true),
                    ReportJson = table.Column<string>(type: "jsonb", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backtest_results", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InstancePositions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstanceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Strategy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VenueAccountKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Symbol = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Side = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Quantity = table.Column<double>(type: "double precision", nullable: false),
                    OrderLabel = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ClientOrderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BrokerPositionId = table.Column<long>(type: "bigint", nullable: false),
                    EntryPrice = table.Column<double>(type: "double precision", nullable: false),
                    PeakPrice = table.Column<double>(type: "double precision", nullable: false),
                    RiskCheckpoint = table.Column<double>(type: "double precision", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ExitPrice = table.Column<double>(type: "double precision", nullable: true),
                    RealizedPnl = table.Column<double>(type: "double precision", nullable: true),
                    AgentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstancePositions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "instruments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AssetClass = table.Column<string>(type: "text", nullable: false),
                    BaseCurrency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    QuoteCurrency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Digits = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsTradable = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instruments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "model_decisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InstanceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PackageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BarTs = table.Column<long>(type: "bigint", nullable: false),
                    Step = table.Column<long>(type: "bigint", nullable: false),
                    InputJson = table.Column<string>(type: "text", nullable: false),
                    Forecast = table.Column<double>(type: "double precision", nullable: true),
                    TargetsJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Fired = table.Column<bool>(type: "boolean", nullable: false),
                    LatencyUs = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_decisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VenueId = table.Column<Guid>(type: "uuid", nullable: true),
                    VenueKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OrderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Symbol = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Side = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Quantity = table.Column<double>(type: "double precision", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    RealizedPnl = table.Column<double>(type: "double precision", nullable: true),
                    ExecutionTs = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "strategy_packages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Author = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ManifestJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PromotionHistory = table.Column<string>(type: "jsonb", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CoreVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_strategy_packages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "trade_signals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StrategyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Instrument = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Side = table.Column<string>(type: "text", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_signals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AvatarPath = table.Column<string>(type: "text", nullable: true),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "venue_owner",
                columns: table => new
                {
                    AccountKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AgentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    HeartbeatAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_venue_owner", x => x.AccountKey);
                });

            migrationBuilder.CreateTable(
                name: "VenueSymbolCatalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    VenueSymbol = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VenueSymbolId = table.Column<long>(type: "bigint", nullable: false),
                    TickSize = table.Column<double>(type: "double precision", nullable: false),
                    ContractSize = table.Column<double>(type: "double precision", nullable: false),
                    MinQuantity = table.Column<double>(type: "double precision", nullable: false),
                    MaxQuantity = table.Column<double>(type: "double precision", nullable: false),
                    QuantityStep = table.Column<double>(type: "double precision", nullable: false),
                    Currency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Tradable = table.Column<bool>(type: "boolean", nullable: false),
                    CatalogVersion = table.Column<long>(type: "bigint", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VenueSymbolCatalog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "walkforward_runs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Entry = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FilesJson = table.Column<string>(type: "jsonb", nullable: false),
                    SymbolsJson = table.Column<string>(type: "text", nullable: false),
                    Timeframe = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    FromTs = table.Column<long>(type: "bigint", nullable: false),
                    ToTs = table.Column<long>(type: "bigint", nullable: false),
                    NFolds = table.Column<int>(type: "integer", nullable: false),
                    TrainSeconds = table.Column<long>(type: "bigint", nullable: false),
                    TestSeconds = table.Column<long>(type: "bigint", nullable: false),
                    PurgeSeconds = table.Column<long>(type: "bigint", nullable: false),
                    EmbargoSeconds = table.Column<long>(type: "bigint", nullable: false),
                    FoldsJson = table.Column<string>(type: "jsonb", nullable: false),
                    SummaryJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_walkforward_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "addresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Street = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    City = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    State = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ZipCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Country = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_addresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_addresses_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "alpaca_venues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ApiKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ApiSecret = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsLive = table.Column<bool>(type: "boolean", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alpaca_venues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_alpaca_venues_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "build_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    JobId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Phase = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CancelRequested = table.Column<bool>(type: "boolean", nullable: false),
                    Error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    LogTail = table.Column<string>(type: "text", nullable: false),
                    PackageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Entry = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SymbolsJson = table.Column<string>(type: "text", nullable: false),
                    Timeframe = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    FromTs = table.Column<long>(type: "bigint", nullable: false),
                    ToTs = table.Column<long>(type: "bigint", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_build_jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_build_jobs_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ctrader_venues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<long>(type: "bigint", maxLength: 100, nullable: false),
                    Broker = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    IsLive = table.Column<bool>(type: "boolean", nullable: false),
                    AccessToken = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RefreshToken = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Approved = table.Column<bool>(type: "boolean", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CredentialsVersion = table.Column<long>(type: "bigint", nullable: false),
                    LastRefreshError = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IsMarketFeed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ctrader_venues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ctrader_venues_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hyperliquid_venues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PrivateKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsLive = table.Column<bool>(type: "boolean", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hyperliquid_venues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_hyperliquid_venues_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "metatrader_terminals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Login = table.Column<long>(type: "bigint", nullable: false),
                    Password = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Server = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metatrader_terminals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metatrader_terminals_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "strategy_instances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DegradePolicy = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    ExecutionMode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    InstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Feed = table.Column<string>(type: "text", nullable: true),
                    RiskMode = table.Column<string>(type: "text", nullable: true),
                    RiskPercent = table.Column<double>(type: "double precision", nullable: true),
                    RiskLots = table.Column<double>(type: "double precision", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_strategy_instances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_strategy_instances_instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "instruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_strategy_instances_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_information",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Gender = table.Column<string>(type: "text", nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_information", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_information_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "instrument_mappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CTraderVenueId = table.Column<Guid>(type: "uuid", nullable: true),
                    MetaTraderTerminalId = table.Column<Guid>(type: "uuid", nullable: true),
                    AlpacaVenueId = table.Column<Guid>(type: "uuid", nullable: true),
                    HyperliquidVenueId = table.Column<Guid>(type: "uuid", nullable: true),
                    Symbol = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SymbolId = table.Column<long>(type: "bigint", nullable: false),
                    VolumeStep = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    MinVolume = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    MaxVolume = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CatalogVersion = table.Column<long>(type: "bigint", nullable: false),
                    Invalid = table.Column<bool>(type: "boolean", nullable: false),
                    InvalidReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instrument_mappings", x => x.Id);
                    table.CheckConstraint("instrument_mapping_exactly_one_venue", "num_nonnulls(\"CTraderVenueId\", \"MetaTraderTerminalId\", \"AlpacaVenueId\", \"HyperliquidVenueId\") = 1");
                    table.ForeignKey(
                        name: "FK_instrument_mappings_alpaca_venues_AlpacaVenueId",
                        column: x => x.AlpacaVenueId,
                        principalTable: "alpaca_venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_instrument_mappings_ctrader_venues_CTraderVenueId",
                        column: x => x.CTraderVenueId,
                        principalTable: "ctrader_venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_instrument_mappings_hyperliquid_venues_HyperliquidVenueId",
                        column: x => x.HyperliquidVenueId,
                        principalTable: "hyperliquid_venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_instrument_mappings_instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "instruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_instrument_mappings_metatrader_terminals_MetaTraderTerminal~",
                        column: x => x.MetaTraderTerminalId,
                        principalTable: "metatrader_terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feed_instances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VenueInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feed_instances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "venue_instances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VenueId = table.Column<Guid>(type: "uuid", nullable: true),
                    AlpacaVenueId = table.Column<Guid>(type: "uuid", nullable: true),
                    HyperliquidVenueId = table.Column<Guid>(type: "uuid", nullable: true),
                    MetaTraderTerminalId = table.Column<Guid>(type: "uuid", nullable: true),
                    StrategyInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "text", nullable: false),
                    FeedInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    FeedSource = table.Column<string>(type: "text", nullable: true),
                    Legs = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_venue_instances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_venue_instances_alpaca_venues_AlpacaVenueId",
                        column: x => x.AlpacaVenueId,
                        principalTable: "alpaca_venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_venue_instances_ctrader_venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "ctrader_venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_venue_instances_feed_instances_FeedInstanceId",
                        column: x => x.FeedInstanceId,
                        principalTable: "feed_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_venue_instances_hyperliquid_venues_HyperliquidVenueId",
                        column: x => x.HyperliquidVenueId,
                        principalTable: "hyperliquid_venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_venue_instances_metatrader_terminals_MetaTraderTerminalId",
                        column: x => x.MetaTraderTerminalId,
                        principalTable: "metatrader_terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_venue_instances_strategy_instances_StrategyInstanceId",
                        column: x => x.StrategyInstanceId,
                        principalTable: "strategy_instances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_addresses_UserId",
                table: "addresses",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_alpaca_venues_UserId",
                table: "alpaca_venues",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_backtest_results_Status",
                table: "backtest_results",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_build_jobs_JobId",
                table: "build_jobs",
                column: "JobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_build_jobs_Status",
                table: "build_jobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_build_jobs_UserId",
                table: "build_jobs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ctrader_venues_UserId",
                table: "ctrader_venues",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_feed_instances_VenueInstanceId",
                table: "feed_instances",
                column: "VenueInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_hyperliquid_venues_UserId",
                table: "hyperliquid_venues",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_InstancePositions_InstanceId_State",
                table: "InstancePositions",
                columns: new[] { "InstanceId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_InstancePositions_VenueAccountKey_State",
                table: "InstancePositions",
                columns: new[] { "VenueAccountKey", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_instrument_mappings_AlpacaVenueId",
                table: "instrument_mappings",
                column: "AlpacaVenueId");

            migrationBuilder.CreateIndex(
                name: "IX_instrument_mappings_CTraderVenueId",
                table: "instrument_mappings",
                column: "CTraderVenueId");

            migrationBuilder.CreateIndex(
                name: "IX_instrument_mappings_HyperliquidVenueId",
                table: "instrument_mappings",
                column: "HyperliquidVenueId");

            migrationBuilder.CreateIndex(
                name: "IX_instrument_mappings_InstrumentId",
                table: "instrument_mappings",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_instrument_mappings_MetaTraderTerminalId",
                table: "instrument_mappings",
                column: "MetaTraderTerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_instruments_Symbol",
                table: "instruments",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_metatrader_terminals_Login_Server",
                table: "metatrader_terminals",
                columns: new[] { "Login", "Server" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_metatrader_terminals_UserId",
                table: "metatrader_terminals",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_model_decisions_InstanceId_BarTs",
                table: "model_decisions",
                columns: new[] { "InstanceId", "BarTs" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_strategy_instances_InstrumentId",
                table: "strategy_instances",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_strategy_instances_UserId",
                table: "strategy_instances",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_user_information_UserId",
                table: "user_information",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "users",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "users",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_venue_instances_AlpacaVenueId",
                table: "venue_instances",
                column: "AlpacaVenueId");

            migrationBuilder.CreateIndex(
                name: "IX_venue_instances_FeedInstanceId",
                table: "venue_instances",
                column: "FeedInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_venue_instances_HyperliquidVenueId",
                table: "venue_instances",
                column: "HyperliquidVenueId");

            migrationBuilder.CreateIndex(
                name: "IX_venue_instances_MetaTraderTerminalId",
                table: "venue_instances",
                column: "MetaTraderTerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_venue_instances_StrategyInstanceId",
                table: "venue_instances",
                column: "StrategyInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_venue_instances_VenueId",
                table: "venue_instances",
                column: "VenueId");

            migrationBuilder.CreateIndex(
                name: "IX_VenueSymbolCatalog_AccountKey_SyncedAt",
                table: "VenueSymbolCatalog",
                columns: new[] { "AccountKey", "SyncedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VenueSymbolCatalog_AccountKey_VenueSymbol",
                table: "VenueSymbolCatalog",
                columns: new[] { "AccountKey", "VenueSymbol" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_walkforward_runs_Status",
                table: "walkforward_runs",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_feed_instances_venue_instances_VenueInstanceId",
                table: "feed_instances",
                column: "VenueInstanceId",
                principalTable: "venue_instances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_alpaca_venues_users_UserId",
                table: "alpaca_venues");

            migrationBuilder.DropForeignKey(
                name: "FK_ctrader_venues_users_UserId",
                table: "ctrader_venues");

            migrationBuilder.DropForeignKey(
                name: "FK_hyperliquid_venues_users_UserId",
                table: "hyperliquid_venues");

            migrationBuilder.DropForeignKey(
                name: "FK_metatrader_terminals_users_UserId",
                table: "metatrader_terminals");

            migrationBuilder.DropForeignKey(
                name: "FK_strategy_instances_users_UserId",
                table: "strategy_instances");

            migrationBuilder.DropForeignKey(
                name: "FK_feed_instances_venue_instances_VenueInstanceId",
                table: "feed_instances");

            migrationBuilder.DropTable(
                name: "addresses");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "backtest_results");

            migrationBuilder.DropTable(
                name: "build_jobs");

            migrationBuilder.DropTable(
                name: "InstancePositions");

            migrationBuilder.DropTable(
                name: "instrument_mappings");

            migrationBuilder.DropTable(
                name: "model_decisions");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "strategy_packages");

            migrationBuilder.DropTable(
                name: "trade_signals");

            migrationBuilder.DropTable(
                name: "user_information");

            migrationBuilder.DropTable(
                name: "venue_owner");

            migrationBuilder.DropTable(
                name: "VenueSymbolCatalog");

            migrationBuilder.DropTable(
                name: "walkforward_runs");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "venue_instances");

            migrationBuilder.DropTable(
                name: "alpaca_venues");

            migrationBuilder.DropTable(
                name: "ctrader_venues");

            migrationBuilder.DropTable(
                name: "feed_instances");

            migrationBuilder.DropTable(
                name: "hyperliquid_venues");

            migrationBuilder.DropTable(
                name: "metatrader_terminals");

            migrationBuilder.DropTable(
                name: "strategy_instances");

            migrationBuilder.DropTable(
                name: "instruments");
        }
    }
}
