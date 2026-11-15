using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class AddFleetVenueRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "instrument_mapping_exactly_one_venue",
                table: "instrument_mappings");

            migrationBuilder.AddColumn<Guid>(
                name: "FleetVenueId",
                table: "venue_instances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FleetVenueId",
                table: "instrument_mappings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_venue_instances_FleetVenueId",
                table: "venue_instances",
                column: "FleetVenueId");

            migrationBuilder.CreateIndex(
                name: "IX_instrument_mappings_FleetVenueId",
                table: "instrument_mappings",
                column: "FleetVenueId");

            migrationBuilder.AddCheckConstraint(
                name: "instrument_mapping_exactly_one_venue",
                table: "instrument_mappings",
                sql: "num_nonnulls(\"CTraderVenueId\", \"MetaTraderTerminalId\", \"AlpacaVenueId\", \"HyperliquidVenueId\", \"IbkrVenueId\", \"FleetVenueId\") = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_instrument_mappings_fleet_venues_FleetVenueId",
                table: "instrument_mappings",
                column: "FleetVenueId",
                principalTable: "fleet_venues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_venue_instances_fleet_venues_FleetVenueId",
                table: "venue_instances",
                column: "FleetVenueId",
                principalTable: "fleet_venues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_instrument_mappings_fleet_venues_FleetVenueId",
                table: "instrument_mappings");

            migrationBuilder.DropForeignKey(
                name: "FK_venue_instances_fleet_venues_FleetVenueId",
                table: "venue_instances");

            migrationBuilder.DropIndex(
                name: "IX_venue_instances_FleetVenueId",
                table: "venue_instances");

            migrationBuilder.DropIndex(
                name: "IX_instrument_mappings_FleetVenueId",
                table: "instrument_mappings");

            migrationBuilder.DropCheckConstraint(
                name: "instrument_mapping_exactly_one_venue",
                table: "instrument_mappings");

            migrationBuilder.DropColumn(
                name: "FleetVenueId",
                table: "venue_instances");

            migrationBuilder.DropColumn(
                name: "FleetVenueId",
                table: "instrument_mappings");

            migrationBuilder.AddCheckConstraint(
                name: "instrument_mapping_exactly_one_venue",
                table: "instrument_mappings",
                sql: "num_nonnulls(\"CTraderVenueId\", \"MetaTraderTerminalId\", \"AlpacaVenueId\", \"HyperliquidVenueId\", \"IbkrVenueId\") = 1");
        }
    }
}
