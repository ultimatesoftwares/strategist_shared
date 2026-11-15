using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class RenameMetaTraderTerminalsToMetaTraderVenues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_metatrader_terminals_users_UserId",
                table: "metatrader_terminals");

            migrationBuilder.DropForeignKey(
                name: "FK_venue_instances_metatrader_terminals_MetaTraderTerminalId",
                table: "venue_instances");

            migrationBuilder.DropPrimaryKey(
                name: "PK_metatrader_terminals",
                table: "metatrader_terminals");

            migrationBuilder.RenameTable(
                name: "metatrader_terminals",
                newName: "metatrader_venues");

            migrationBuilder.RenameIndex(
                name: "IX_metatrader_terminals_UserId",
                table: "metatrader_venues",
                newName: "IX_metatrader_venues_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_metatrader_terminals_Login_Server",
                table: "metatrader_venues",
                newName: "IX_metatrader_venues_Login_Server");

            migrationBuilder.AddPrimaryKey(
                name: "PK_metatrader_venues",
                table: "metatrader_venues",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_metatrader_venues_users_UserId",
                table: "metatrader_venues",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_venue_instances_metatrader_venues_MetaTraderTerminalId",
                table: "venue_instances",
                column: "MetaTraderTerminalId",
                principalTable: "metatrader_venues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_metatrader_venues_users_UserId",
                table: "metatrader_venues");

            migrationBuilder.DropForeignKey(
                name: "FK_venue_instances_metatrader_venues_MetaTraderTerminalId",
                table: "venue_instances");

            migrationBuilder.DropPrimaryKey(
                name: "PK_metatrader_venues",
                table: "metatrader_venues");

            migrationBuilder.RenameTable(
                name: "metatrader_venues",
                newName: "metatrader_terminals");

            migrationBuilder.RenameIndex(
                name: "IX_metatrader_venues_UserId",
                table: "metatrader_terminals",
                newName: "IX_metatrader_terminals_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_metatrader_venues_Login_Server",
                table: "metatrader_terminals",
                newName: "IX_metatrader_terminals_Login_Server");

            migrationBuilder.AddPrimaryKey(
                name: "PK_metatrader_terminals",
                table: "metatrader_terminals",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_metatrader_terminals_users_UserId",
                table: "metatrader_terminals",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_venue_instances_metatrader_terminals_MetaTraderTerminalId",
                table: "venue_instances",
                column: "MetaTraderTerminalId",
                principalTable: "metatrader_terminals",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
