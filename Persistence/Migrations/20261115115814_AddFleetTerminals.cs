using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class AddFleetTerminals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fleet_terminals",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HostId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Platform = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    HomeDir = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Command = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ArgsJson = table.Column<string>(type: "jsonb", nullable: false),
                    WorkingDir = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    EnvironmentJson = table.Column<string>(type: "jsonb", nullable: false),
                    AutoStart = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fleet_terminals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fleet_terminals_HostId",
                table: "fleet_terminals",
                column: "HostId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fleet_terminals");
        }
    }
}
