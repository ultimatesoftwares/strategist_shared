using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class DropEngineOrchestration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_feed_instances_venue_instances_VenueInstanceId",
                table: "feed_instances");

            migrationBuilder.DropTable(
                name: "InstancePositions");

            migrationBuilder.DropTable(
                name: "venue_owner");

            migrationBuilder.DropTable(
                name: "venue_instances");

            migrationBuilder.DropTable(
                name: "feed_instances");

            migrationBuilder.DropTable(
                name: "metatrader_venues");

            migrationBuilder.DropTable(
                name: "strategy_instances");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InstancePositions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BrokerPositionId = table.Column<long>(type: "bigint", nullable: false),
                    ClientOrderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EntryPrice = table.Column<double>(type: "double precision", nullable: false),
                    ExitPrice = table.Column<double>(type: "double precision", nullable: true),
                    InstanceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OrderLabel = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    PeakPrice = table.Column<double>(type: "double precision", nullable: false),
                    Quantity = table.Column<double>(type: "double precision", nullable: false),
                    RealizedPnl = table.Column<double>(type: "double precision", nullable: true),
                    RiskCheckpoint = table.Column<double>(type: "double precision", nullable: false),
                    Side = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Strategy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Symbol = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VenueAccountKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstancePositions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "metatrader_venues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Login = table.Column<long>(type: "bigint", nullable: false),
                    Password = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Server = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metatrader_venues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_metatrader_venues_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "strategy_instances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DegradePolicy = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    ExecutionMode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Feed = table.Column<string>(type: "text", nullable: true),
                    PackageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RiskLots = table.Column<double>(type: "double precision", nullable: true),
                    RiskMode = table.Column<string>(type: "text", nullable: true),
                    RiskPercent = table.Column<double>(type: "double precision", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
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
                name: "venue_owner",
                columns: table => new
                {
                    AccountKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AgentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HeartbeatAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_venue_owner", x => x.AccountKey);
                });

            migrationBuilder.CreateTable(
                name: "feed_instances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VenueInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Platform = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
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
                    AlpacaVenueId = table.Column<Guid>(type: "uuid", nullable: true),
                    FeedInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    MetaTraderTerminalId = table.Column<Guid>(type: "uuid", nullable: true),
                    StrategyInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    VenueId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FeedSource = table.Column<string>(type: "text", nullable: true),
                    FleetVenueId = table.Column<Guid>(type: "uuid", nullable: true),
                    Legs = table.Column<string>(type: "text", nullable: true),
                    Platform = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
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
                        name: "FK_venue_instances_metatrader_venues_MetaTraderTerminalId",
                        column: x => x.MetaTraderTerminalId,
                        principalTable: "metatrader_venues",
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
                name: "IX_feed_instances_VenueInstanceId",
                table: "feed_instances",
                column: "VenueInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_InstancePositions_InstanceId_State",
                table: "InstancePositions",
                columns: new[] { "InstanceId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_InstancePositions_VenueAccountKey_State",
                table: "InstancePositions",
                columns: new[] { "VenueAccountKey", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_metatrader_venues_Login_Server",
                table: "metatrader_venues",
                columns: new[] { "Login", "Server" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_metatrader_venues_UserId",
                table: "metatrader_venues",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_strategy_instances_InstrumentId",
                table: "strategy_instances",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_strategy_instances_UserId",
                table: "strategy_instances",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_venue_instances_AlpacaVenueId",
                table: "venue_instances",
                column: "AlpacaVenueId");

            migrationBuilder.CreateIndex(
                name: "IX_venue_instances_FeedInstanceId",
                table: "venue_instances",
                column: "FeedInstanceId");

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

            migrationBuilder.AddForeignKey(
                name: "FK_feed_instances_venue_instances_VenueInstanceId",
                table: "feed_instances",
                column: "VenueInstanceId",
                principalTable: "venue_instances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
