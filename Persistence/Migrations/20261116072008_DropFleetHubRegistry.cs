using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class DropFleetHubRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_venue_instances_fleet_venue_registrations_FleetVenueId",
                table: "venue_instances");

            migrationBuilder.DropTable(
                name: "fleet_api_keys");

            migrationBuilder.DropTable(
                name: "fleet_instrument_mappings");

            migrationBuilder.DropTable(
                name: "fleet_venue_instruments");

            migrationBuilder.DropTable(
                name: "fleet_venue_registrations");

            migrationBuilder.DropTable(
                name: "fleet_venues");

            migrationBuilder.DropIndex(
                name: "IX_venue_instances_FleetVenueId",
                table: "venue_instances");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fleet_venue_registrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApiKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsLive = table.Column<bool>(type: "boolean", nullable: false),
                    RestUrl = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VenueId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fleet_venue_registrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fleet_venue_registrations_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "fleet_venues",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AccountLogin = table.Column<long>(type: "bigint", nullable: false),
                    AccountUsername = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BrokerName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BrokerServer = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CredentialsJson = table.Column<string>(type: "jsonb", nullable: true),
                    Platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SidecarToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fleet_venues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "fleet_api_keys",
                columns: table => new
                {
                    KeyId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    VenueId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fleet_api_keys", x => x.KeyId);
                    table.ForeignKey(
                        name: "FK_fleet_api_keys_fleet_venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "fleet_venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fleet_instrument_mappings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VenueId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CanonicalInstrumentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    VenueSymbol = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fleet_instrument_mappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fleet_instrument_mappings_fleet_venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "fleet_venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fleet_venue_instruments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VenueId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContractSize = table.Column<double>(type: "double precision", nullable: false),
                    CurrencyBase = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CurrencyMargin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CurrencyProfit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Digits = table.Column<int>(type: "integer", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TradeAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VolumeMax = table.Column<double>(type: "double precision", nullable: false),
                    VolumeMin = table.Column<double>(type: "double precision", nullable: false),
                    VolumeStep = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fleet_venue_instruments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fleet_venue_instruments_fleet_venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "fleet_venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_venue_instances_FleetVenueId",
                table: "venue_instances",
                column: "FleetVenueId");

            migrationBuilder.CreateIndex(
                name: "IX_fleet_api_keys_VenueId",
                table: "fleet_api_keys",
                column: "VenueId");

            migrationBuilder.CreateIndex(
                name: "IX_fleet_instrument_mappings_VenueId_CanonicalInstrumentId",
                table: "fleet_instrument_mappings",
                columns: new[] { "VenueId", "CanonicalInstrumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fleet_venue_instruments_VenueId_Symbol",
                table: "fleet_venue_instruments",
                columns: new[] { "VenueId", "Symbol" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fleet_venue_registrations_UserId",
                table: "fleet_venue_registrations",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_fleet_venue_registrations_VenueId",
                table: "fleet_venue_registrations",
                column: "VenueId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fleet_venues_ClientId",
                table: "fleet_venues",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fleet_venues_SidecarToken",
                table: "fleet_venues",
                column: "SidecarToken",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_venue_instances_fleet_venue_registrations_FleetVenueId",
                table: "venue_instances",
                column: "FleetVenueId",
                principalTable: "fleet_venue_registrations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
