using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <summary>
    /// The cTrader Open API / OAuth onboarding is retired: venues are captured
    /// from FIX credentials alone (broker identity inferred from the
    /// SenderCompID), so the OAuth token columns and the Open-API-era
    /// market-feed flag go away.
    /// </summary>
    public partial class DropCTraderOpenApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AccessToken", table: "ctrader_venues");
            migrationBuilder.DropColumn(name: "RefreshToken", table: "ctrader_venues");
            migrationBuilder.DropColumn(name: "ExpiresAt", table: "ctrader_venues");
            migrationBuilder.DropColumn(name: "CredentialsVersion", table: "ctrader_venues");
            migrationBuilder.DropColumn(name: "LastRefreshError", table: "ctrader_venues");
            migrationBuilder.DropColumn(name: "IsMarketFeed", table: "ctrader_venues");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "AccessToken", table: "ctrader_venues",
                type: "character varying(512)", maxLength: 512, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "RefreshToken", table: "ctrader_venues",
                type: "character varying(512)", maxLength: 512, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<DateTime>(name: "ExpiresAt", table: "ctrader_venues",
                type: "timestamp with time zone", nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));
            migrationBuilder.AddColumn<long>(name: "CredentialsVersion", table: "ctrader_venues",
                type: "bigint", nullable: false, defaultValue: 0L);
            migrationBuilder.AddColumn<string>(name: "LastRefreshError", table: "ctrader_venues",
                type: "character varying(300)", maxLength: 300, nullable: true);
            migrationBuilder.AddColumn<bool>(name: "IsMarketFeed", table: "ctrader_venues",
                type: "boolean", nullable: false, defaultValue: false);
        }
    }
}
