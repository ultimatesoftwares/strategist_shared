using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTerminalCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "VenueId",
                table: "fleet_venues",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "fleet_terminals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "VenueId",
                table: "fleet_instrument_mappings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "TerminalId",
                table: "fleet_api_keys",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.CreateTable(
                name: "fleet_terminal_instruments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TerminalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Symbol = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Digits = table.Column<int>(type: "integer", nullable: false),
                    ContractSize = table.Column<double>(type: "double precision", nullable: false),
                    VolumeMin = table.Column<double>(type: "double precision", nullable: false),
                    VolumeMax = table.Column<double>(type: "double precision", nullable: false),
                    VolumeStep = table.Column<double>(type: "double precision", nullable: false),
                    CurrencyBase = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CurrencyProfit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CurrencyMargin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TradeAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fleet_terminal_instruments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fleet_terminal_instruments_fleet_terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "fleet_terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fleet_terminal_instruments_TerminalId_Symbol",
                table: "fleet_terminal_instruments",
                columns: new[] { "TerminalId", "Symbol" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fleet_terminal_instruments");

            migrationBuilder.AlterColumn<string>(
                name: "VenueId",
                table: "fleet_venues",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Id",
                table: "fleet_terminals",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "VenueId",
                table: "fleet_instrument_mappings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "TerminalId",
                table: "fleet_api_keys",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);
        }
    }
}
