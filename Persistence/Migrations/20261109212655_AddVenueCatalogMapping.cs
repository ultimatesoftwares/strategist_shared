using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class AddVenueCatalogMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssetClass",
                table: "VenueSymbolCatalog",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "VenueSymbolCatalog",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "mapped_to",
                table: "VenueSymbolCatalog",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VenueSymbolCatalog_mapped_to",
                table: "VenueSymbolCatalog",
                column: "mapped_to");

            migrationBuilder.AddForeignKey(
                name: "FK_VenueSymbolCatalog_instruments_mapped_to",
                table: "VenueSymbolCatalog",
                column: "mapped_to",
                principalTable: "instruments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VenueSymbolCatalog_instruments_mapped_to",
                table: "VenueSymbolCatalog");

            migrationBuilder.DropIndex(
                name: "IX_VenueSymbolCatalog_mapped_to",
                table: "VenueSymbolCatalog");

            migrationBuilder.DropColumn(
                name: "AssetClass",
                table: "VenueSymbolCatalog");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "VenueSymbolCatalog");

            migrationBuilder.DropColumn(
                name: "mapped_to",
                table: "VenueSymbolCatalog");
        }
    }
}
