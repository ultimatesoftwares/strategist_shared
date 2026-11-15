using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class AddFleetVenueRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fleet_venues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    VenueId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RestUrl = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ApiKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsLive = table.Column<bool>(type: "boolean", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fleet_venues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fleet_venues_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_fleet_venues_UserId",
                table: "fleet_venues",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_fleet_venues_VenueId",
                table: "fleet_venues",
                column: "VenueId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fleet_venues");
        }
    }
}
