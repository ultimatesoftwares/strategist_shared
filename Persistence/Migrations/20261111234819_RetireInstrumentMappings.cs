using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class RetireInstrumentMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // instrument_mappings is retired: a catalog row's mapped_to stamp
            // (→ instruments.Id) is the single mapping mechanism now, so the
            // per-venue mapping rows go away entirely.
            migrationBuilder.DropTable(
                name: "instrument_mappings");

            // VenueSymbolCatalog → instrument_catalog: hand-rewritten from the
            // scaffolded drop+create to a DATA-PRESERVING rename (the rows —
            // including mapped_to stamps — survive). Constraint renames are raw
            // SQL: MigrationBuilder has no RenamePrimaryKey/RenameForeignKey,
            // and production migrations only ever run on PostgreSQL.
            migrationBuilder.RenameTable(
                name: "VenueSymbolCatalog",
                newName: "instrument_catalog");

            migrationBuilder.Sql(
                """ALTER TABLE instrument_catalog RENAME CONSTRAINT "PK_VenueSymbolCatalog" TO "PK_instrument_catalog";""");
            migrationBuilder.Sql(
                """ALTER TABLE instrument_catalog RENAME CONSTRAINT "FK_VenueSymbolCatalog_instruments_mapped_to" TO "FK_instrument_catalog_instruments_mapped_to";""");

            migrationBuilder.RenameIndex(
                name: "IX_VenueSymbolCatalog_AccountKey_SyncedAt",
                table: "instrument_catalog",
                newName: "IX_instrument_catalog_AccountKey_SyncedAt");

            migrationBuilder.RenameIndex(
                name: "IX_VenueSymbolCatalog_AccountKey_VenueSymbol",
                table: "instrument_catalog",
                newName: "IX_instrument_catalog_AccountKey_VenueSymbol");

            migrationBuilder.RenameIndex(
                name: "IX_VenueSymbolCatalog_mapped_to",
                table: "instrument_catalog",
                newName: "IX_instrument_catalog_mapped_to");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse of the data-preserving rename.
            migrationBuilder.RenameIndex(
                name: "IX_instrument_catalog_mapped_to",
                table: "instrument_catalog",
                newName: "IX_VenueSymbolCatalog_mapped_to");

            migrationBuilder.RenameIndex(
                name: "IX_instrument_catalog_AccountKey_VenueSymbol",
                table: "instrument_catalog",
                newName: "IX_VenueSymbolCatalog_AccountKey_VenueSymbol");

            migrationBuilder.RenameIndex(
                name: "IX_instrument_catalog_AccountKey_SyncedAt",
                table: "instrument_catalog",
                newName: "IX_VenueSymbolCatalog_AccountKey_SyncedAt");

            migrationBuilder.Sql(
                """ALTER TABLE instrument_catalog RENAME CONSTRAINT "FK_instrument_catalog_instruments_mapped_to" TO "FK_VenueSymbolCatalog_instruments_mapped_to";""");
            migrationBuilder.Sql(
                """ALTER TABLE instrument_catalog RENAME CONSTRAINT "PK_instrument_catalog" TO "PK_VenueSymbolCatalog";""");

            migrationBuilder.RenameTable(
                name: "instrument_catalog",
                newName: "VenueSymbolCatalog");

            migrationBuilder.CreateTable(
                name: "instrument_mappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AlpacaVenueId = table.Column<Guid>(type: "uuid", nullable: true),
                    CTraderVenueId = table.Column<Guid>(type: "uuid", nullable: true),
                    FleetVenueId = table.Column<Guid>(type: "uuid", nullable: true),
                    InstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    MetaTraderTerminalId = table.Column<Guid>(type: "uuid", nullable: true),
                    CatalogVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Invalid = table.Column<bool>(type: "boolean", nullable: false),
                    InvalidReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    MaxVolume = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    MinVolume = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SymbolId = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VolumeStep = table.Column<decimal>(type: "numeric(20,8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instrument_mappings", x => x.Id);
                    table.CheckConstraint("instrument_mapping_exactly_one_venue", "num_nonnulls(\"CTraderVenueId\", \"MetaTraderTerminalId\", \"AlpacaVenueId\", \"FleetVenueId\") = 1");
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
                        name: "FK_instrument_mappings_fleet_venue_registrations_FleetVenueId",
                        column: x => x.FleetVenueId,
                        principalTable: "fleet_venue_registrations",
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

            migrationBuilder.CreateIndex(
                name: "IX_instrument_mappings_AlpacaVenueId",
                table: "instrument_mappings",
                column: "AlpacaVenueId");

            migrationBuilder.CreateIndex(
                name: "IX_instrument_mappings_CTraderVenueId",
                table: "instrument_mappings",
                column: "CTraderVenueId");

            migrationBuilder.CreateIndex(
                name: "IX_instrument_mappings_FleetVenueId",
                table: "instrument_mappings",
                column: "FleetVenueId");

            migrationBuilder.CreateIndex(
                name: "IX_instrument_mappings_InstrumentId",
                table: "instrument_mappings",
                column: "InstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_instrument_mappings_MetaTraderTerminalId",
                table: "instrument_mappings",
                column: "MetaTraderTerminalId");
        }
    }
}
