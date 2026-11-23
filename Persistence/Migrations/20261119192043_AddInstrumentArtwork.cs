using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInstrumentArtwork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instrument_artwork",
                columns: table => new
                {
                    InstrumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Icon = table.Column<byte[]>(type: "bytea", nullable: true),
                    BaseIcon = table.Column<byte[]>(type: "bytea", nullable: true),
                    QuoteIcon = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instrument_artwork", x => x.InstrumentId);
                    table.ForeignKey(
                        name: "FK_instrument_artwork_instruments_InstrumentId",
                        column: x => x.InstrumentId,
                        principalTable: "instruments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instrument_artwork");
        }
    }
}
