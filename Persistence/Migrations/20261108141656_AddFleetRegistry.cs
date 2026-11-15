using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class AddFleetRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fleet_terminals",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ClientId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AccountLogin = table.Column<long>(type: "bigint", nullable: false),
                    AccountUsername = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BrokerServer = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BrokerName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SidecarToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fleet_terminals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "fleet_api_keys",
                columns: table => new
                {
                    KeyId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TerminalId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fleet_api_keys", x => x.KeyId);
                    table.ForeignKey(
                        name: "FK_fleet_api_keys_fleet_terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "fleet_terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fleet_instrument_mappings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VenueId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CanonicalInstrumentId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VenueSymbol = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fleet_instrument_mappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fleet_instrument_mappings_fleet_terminals_VenueId",
                        column: x => x.VenueId,
                        principalTable: "fleet_terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fleet_api_keys_TerminalId",
                table: "fleet_api_keys",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_fleet_instrument_mappings_VenueId_CanonicalInstrumentId",
                table: "fleet_instrument_mappings",
                columns: new[] { "VenueId", "CanonicalInstrumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fleet_terminals_ClientId",
                table: "fleet_terminals",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fleet_terminals_SidecarToken",
                table: "fleet_terminals",
                column: "SidecarToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fleet_api_keys");

            migrationBuilder.DropTable(
                name: "fleet_instrument_mappings");

            migrationBuilder.DropTable(
                name: "fleet_terminals");
        }
    }
}
