using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCTraderFixCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "FixCredentialsVersion",
                table: "ctrader_venues",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "FixHost",
                table: "ctrader_venues",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FixLogin",
                table: "ctrader_venues",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FixPassword",
                table: "ctrader_venues",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "FixPort",
                table: "ctrader_venues",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FixSenderCompId",
                table: "ctrader_venues",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "FixSsl",
                table: "ctrader_venues",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FixCredentialsVersion",
                table: "ctrader_venues");

            migrationBuilder.DropColumn(
                name: "FixHost",
                table: "ctrader_venues");

            migrationBuilder.DropColumn(
                name: "FixLogin",
                table: "ctrader_venues");

            migrationBuilder.DropColumn(
                name: "FixPassword",
                table: "ctrader_venues");

            migrationBuilder.DropColumn(
                name: "FixPort",
                table: "ctrader_venues");

            migrationBuilder.DropColumn(
                name: "FixSenderCompId",
                table: "ctrader_venues");

            migrationBuilder.DropColumn(
                name: "FixSsl",
                table: "ctrader_venues");
        }
    }
}
