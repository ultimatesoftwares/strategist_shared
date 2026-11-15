using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class AddIbkrVenues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "instrument_mapping_exactly_one_venue",
                table: "instrument_mappings");

            migrationBuilder.AddColumn<Guid>(
                name: "IbkrVenueId",
                table: "venue_instances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IbkrVenueId",
                table: "instrument_mappings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ibkr_venues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GatewayUsername = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AccountId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TlsThumbprint = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsLive = table.Column<bool>(type: "boolean", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ibkr_venues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ibkr_venues_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_venue_instances_IbkrVenueId",
                table: "venue_instances",
                column: "IbkrVenueId");

            migrationBuilder.CreateIndex(
                name: "IX_instrument_mappings_IbkrVenueId",
                table: "instrument_mappings",
                column: "IbkrVenueId");

            migrationBuilder.AddCheckConstraint(
                name: "instrument_mapping_exactly_one_venue",
                table: "instrument_mappings",
                sql: "num_nonnulls(\"CTraderVenueId\", \"MetaTraderTerminalId\", \"AlpacaVenueId\", \"HyperliquidVenueId\", \"IbkrVenueId\") = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ibkr_venues_UserId",
                table: "ibkr_venues",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_instrument_mappings_ibkr_venues_IbkrVenueId",
                table: "instrument_mappings",
                column: "IbkrVenueId",
                principalTable: "ibkr_venues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_venue_instances_ibkr_venues_IbkrVenueId",
                table: "venue_instances",
                column: "IbkrVenueId",
                principalTable: "ibkr_venues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_instrument_mappings_ibkr_venues_IbkrVenueId",
                table: "instrument_mappings");

            migrationBuilder.DropForeignKey(
                name: "FK_venue_instances_ibkr_venues_IbkrVenueId",
                table: "venue_instances");

            migrationBuilder.DropTable(
                name: "ibkr_venues");

            migrationBuilder.DropIndex(
                name: "IX_venue_instances_IbkrVenueId",
                table: "venue_instances");

            migrationBuilder.DropIndex(
                name: "IX_instrument_mappings_IbkrVenueId",
                table: "instrument_mappings");

            migrationBuilder.DropCheckConstraint(
                name: "instrument_mapping_exactly_one_venue",
                table: "instrument_mappings");

            migrationBuilder.DropColumn(
                name: "IbkrVenueId",
                table: "venue_instances");

            migrationBuilder.DropColumn(
                name: "IbkrVenueId",
                table: "instrument_mappings");

            migrationBuilder.AddCheckConstraint(
                name: "instrument_mapping_exactly_one_venue",
                table: "instrument_mappings",
                sql: "num_nonnulls(\"CTraderVenueId\", \"MetaTraderTerminalId\", \"AlpacaVenueId\", \"HyperliquidVenueId\") = 1");
        }
    }
}
