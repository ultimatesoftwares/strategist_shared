using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class DropBuildJobsAndWalkforwardRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "build_jobs");

            migrationBuilder.DropTable(
                name: "walkforward_runs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "build_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelRequested = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Entry = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FromTs = table.Column<long>(type: "bigint", nullable: false),
                    JobId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LogTail = table.Column<string>(type: "text", nullable: false),
                    PackageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Phase = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SymbolsJson = table.Column<string>(type: "text", nullable: false),
                    Timeframe = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ToTs = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_build_jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_build_jobs_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "walkforward_runs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EmbargoSeconds = table.Column<long>(type: "bigint", nullable: false),
                    Entry = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Error = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    FilesJson = table.Column<string>(type: "jsonb", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FoldsJson = table.Column<string>(type: "jsonb", nullable: false),
                    FromTs = table.Column<long>(type: "bigint", nullable: false),
                    NFolds = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PurgeSeconds = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SummaryJson = table.Column<string>(type: "jsonb", nullable: true),
                    SymbolsJson = table.Column<string>(type: "text", nullable: false),
                    TestSeconds = table.Column<long>(type: "bigint", nullable: false),
                    Timeframe = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ToTs = table.Column<long>(type: "bigint", nullable: false),
                    TrainSeconds = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_walkforward_runs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_build_jobs_JobId",
                table: "build_jobs",
                column: "JobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_build_jobs_Status",
                table: "build_jobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_build_jobs_UserId",
                table: "build_jobs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_walkforward_runs_Status",
                table: "walkforward_runs",
                column: "Status");
        }
    }
}
