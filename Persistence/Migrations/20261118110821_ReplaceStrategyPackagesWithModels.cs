using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceStrategyPackagesWithModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "strategy_packages");

            migrationBuilder.DropColumn(
                name: "PackageId",
                table: "backtest_results");

            migrationBuilder.RenameColumn(
                name: "PackageId",
                table: "project_strategy_links",
                newName: "ModelId");

            migrationBuilder.RenameIndex(
                name: "IX_project_strategy_links_ProjectId_PackageId",
                table: "project_strategy_links",
                newName: "IX_project_strategy_links_ProjectId_ModelId");

            migrationBuilder.CreateTable(
                name: "strategy_models",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    UploadedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_strategy_models", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "strategy_models");

            migrationBuilder.RenameColumn(
                name: "ModelId",
                table: "project_strategy_links",
                newName: "PackageId");

            migrationBuilder.RenameIndex(
                name: "IX_project_strategy_links_ProjectId_ModelId",
                table: "project_strategy_links",
                newName: "IX_project_strategy_links_ProjectId_PackageId");

            migrationBuilder.AddColumn<string>(
                name: "PackageId",
                table: "backtest_results",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "strategy_packages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Author = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CoreVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    ManifestJson = table.Column<string>(type: "jsonb", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PromotionHistory = table.Column<string>(type: "jsonb", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_strategy_packages", x => x.Id);
                });
        }
    }
}
