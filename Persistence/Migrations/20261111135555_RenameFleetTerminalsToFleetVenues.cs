using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <summary>Renames the fleet record layer from terminal to venue
    /// nomenclature: the Server-side registration pointer table fleet_venues
    /// becomes fleet_venue_registrations, the hub registry tables
    /// fleet_terminals / fleet_terminal_instruments become fleet_venues /
    /// fleet_venue_instruments, and fleet_api_keys.TerminalId becomes VenueId.
    /// Hand-rewritten from the scaffolded drop/create into pure renames so
    /// existing rows survive; the Designer snapshot is the scaffolded one.</summary>
    public partial class RenameFleetTerminalsToFleetVenues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Server registration pointers: fleet_venues -> fleet_venue_registrations
            //    (frees the fleet_venues name for the hub registry table).
            migrationBuilder.RenameTable(
                name: "fleet_venues",
                newName: "fleet_venue_registrations");

            migrationBuilder.Sql(
                "ALTER TABLE fleet_venue_registrations RENAME CONSTRAINT \"PK_fleet_venues\" TO \"PK_fleet_venue_registrations\";");

            migrationBuilder.Sql(
                "ALTER TABLE fleet_venue_registrations RENAME CONSTRAINT \"FK_fleet_venues_users_UserId\" TO \"FK_fleet_venue_registrations_users_UserId\";");

            migrationBuilder.RenameIndex(
                name: "IX_fleet_venues_UserId",
                table: "fleet_venue_registrations",
                newName: "IX_fleet_venue_registrations_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_fleet_venues_VenueId",
                table: "fleet_venue_registrations",
                newName: "IX_fleet_venue_registrations_VenueId");

            migrationBuilder.Sql(
                "ALTER TABLE instrument_mappings RENAME CONSTRAINT \"FK_instrument_mappings_fleet_venues_FleetVenueId\" TO \"FK_instrument_mappings_fleet_venue_registrations_FleetVenueId\";");

            migrationBuilder.Sql(
                "ALTER TABLE venue_instances RENAME CONSTRAINT \"FK_venue_instances_fleet_venues_FleetVenueId\" TO \"FK_venue_instances_fleet_venue_registrations_FleetVenueId\";");

            // 2) Hub registry: fleet_terminals -> fleet_venues.
            migrationBuilder.RenameTable(
                name: "fleet_terminals",
                newName: "fleet_venues");

            migrationBuilder.Sql(
                "ALTER TABLE fleet_venues RENAME CONSTRAINT \"PK_fleet_terminals\" TO \"PK_fleet_venues\";");

            migrationBuilder.RenameIndex(
                name: "IX_fleet_terminals_ClientId",
                table: "fleet_venues",
                newName: "IX_fleet_venues_ClientId");

            migrationBuilder.RenameIndex(
                name: "IX_fleet_terminals_SidecarToken",
                table: "fleet_venues",
                newName: "IX_fleet_venues_SidecarToken");

            migrationBuilder.Sql(
                "ALTER TABLE fleet_instrument_mappings RENAME CONSTRAINT \"FK_fleet_instrument_mappings_fleet_terminals_VenueId\" TO \"FK_fleet_instrument_mappings_fleet_venues_VenueId\";");

            migrationBuilder.RenameColumn(
                name: "TerminalId",
                table: "fleet_api_keys",
                newName: "VenueId");

            migrationBuilder.RenameIndex(
                name: "IX_fleet_api_keys_TerminalId",
                table: "fleet_api_keys",
                newName: "IX_fleet_api_keys_VenueId");

            migrationBuilder.Sql(
                "ALTER TABLE fleet_api_keys RENAME CONSTRAINT \"FK_fleet_api_keys_fleet_terminals_TerminalId\" TO \"FK_fleet_api_keys_fleet_venues_VenueId\";");

            // 3) Hub catalog: fleet_terminal_instruments -> fleet_venue_instruments.
            migrationBuilder.RenameTable(
                name: "fleet_terminal_instruments",
                newName: "fleet_venue_instruments");

            migrationBuilder.Sql(
                "ALTER TABLE fleet_venue_instruments RENAME CONSTRAINT \"PK_fleet_terminal_instruments\" TO \"PK_fleet_venue_instruments\";");

            migrationBuilder.RenameColumn(
                name: "TerminalId",
                table: "fleet_venue_instruments",
                newName: "VenueId");

            migrationBuilder.RenameIndex(
                name: "IX_fleet_terminal_instruments_TerminalId_Symbol",
                table: "fleet_venue_instruments",
                newName: "IX_fleet_venue_instruments_VenueId_Symbol");

            migrationBuilder.Sql(
                "ALTER TABLE fleet_venue_instruments RENAME CONSTRAINT \"FK_fleet_terminal_instruments_fleet_terminals_TerminalId\" TO \"FK_fleet_venue_instruments_fleet_venues_VenueId\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 3) fleet_venue_instruments -> fleet_terminal_instruments.
            migrationBuilder.Sql(
                "ALTER TABLE fleet_venue_instruments RENAME CONSTRAINT \"FK_fleet_venue_instruments_fleet_venues_VenueId\" TO \"FK_fleet_terminal_instruments_fleet_terminals_TerminalId\";");

            migrationBuilder.RenameIndex(
                name: "IX_fleet_venue_instruments_VenueId_Symbol",
                table: "fleet_venue_instruments",
                newName: "IX_fleet_terminal_instruments_TerminalId_Symbol");

            migrationBuilder.RenameColumn(
                name: "VenueId",
                table: "fleet_venue_instruments",
                newName: "TerminalId");

            migrationBuilder.Sql(
                "ALTER TABLE fleet_venue_instruments RENAME CONSTRAINT \"PK_fleet_venue_instruments\" TO \"PK_fleet_terminal_instruments\";");

            migrationBuilder.RenameTable(
                name: "fleet_venue_instruments",
                newName: "fleet_terminal_instruments");

            // 2) fleet_venues -> fleet_terminals.
            migrationBuilder.Sql(
                "ALTER TABLE fleet_api_keys RENAME CONSTRAINT \"FK_fleet_api_keys_fleet_venues_VenueId\" TO \"FK_fleet_api_keys_fleet_terminals_TerminalId\";");

            migrationBuilder.RenameIndex(
                name: "IX_fleet_api_keys_VenueId",
                table: "fleet_api_keys",
                newName: "IX_fleet_api_keys_TerminalId");

            migrationBuilder.RenameColumn(
                name: "VenueId",
                table: "fleet_api_keys",
                newName: "TerminalId");

            migrationBuilder.Sql(
                "ALTER TABLE fleet_instrument_mappings RENAME CONSTRAINT \"FK_fleet_instrument_mappings_fleet_venues_VenueId\" TO \"FK_fleet_instrument_mappings_fleet_terminals_VenueId\";");

            migrationBuilder.RenameIndex(
                name: "IX_fleet_venues_SidecarToken",
                table: "fleet_venues",
                newName: "IX_fleet_terminals_SidecarToken");

            migrationBuilder.RenameIndex(
                name: "IX_fleet_venues_ClientId",
                table: "fleet_venues",
                newName: "IX_fleet_terminals_ClientId");

            migrationBuilder.Sql(
                "ALTER TABLE fleet_venues RENAME CONSTRAINT \"PK_fleet_venues\" TO \"PK_fleet_terminals\";");

            migrationBuilder.RenameTable(
                name: "fleet_venues",
                newName: "fleet_terminals");

            // 1) fleet_venue_registrations -> fleet_venues.
            migrationBuilder.Sql(
                "ALTER TABLE venue_instances RENAME CONSTRAINT \"FK_venue_instances_fleet_venue_registrations_FleetVenueId\" TO \"FK_venue_instances_fleet_venues_FleetVenueId\";");

            migrationBuilder.Sql(
                "ALTER TABLE instrument_mappings RENAME CONSTRAINT \"FK_instrument_mappings_fleet_venue_registrations_FleetVenueId\" TO \"FK_instrument_mappings_fleet_venues_FleetVenueId\";");

            migrationBuilder.RenameIndex(
                name: "IX_fleet_venue_registrations_VenueId",
                table: "fleet_venue_registrations",
                newName: "IX_fleet_venues_VenueId");

            migrationBuilder.RenameIndex(
                name: "IX_fleet_venue_registrations_UserId",
                table: "fleet_venue_registrations",
                newName: "IX_fleet_venues_UserId");

            migrationBuilder.Sql(
                "ALTER TABLE fleet_venue_registrations RENAME CONSTRAINT \"FK_fleet_venue_registrations_users_UserId\" TO \"FK_fleet_venues_users_UserId\";");

            migrationBuilder.Sql(
                "ALTER TABLE fleet_venue_registrations RENAME CONSTRAINT \"PK_fleet_venue_registrations\" TO \"PK_fleet_venues\";");

            migrationBuilder.RenameTable(
                name: "fleet_venue_registrations",
                newName: "fleet_venues");
        }
    }
}
