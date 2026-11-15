using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class AddProvisionJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "provision_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    VenueId = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SymbolId = table.Column<long>(type: "bigint", nullable: true),
                    Timeframe = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    FromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Progress = table.Column<double>(type: "double precision", nullable: false),
                    WrittenRows = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provision_jobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_provision_jobs_Status",
                table: "provision_jobs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provision_jobs");
        }
    }
}
