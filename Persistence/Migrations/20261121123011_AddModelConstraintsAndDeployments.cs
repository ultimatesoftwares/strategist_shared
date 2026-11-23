using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddModelConstraintsAndDeployments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccountType",
                table: "strategy_models",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "all");

            migrationBuilder.AddColumn<decimal>(
                name: "MinBalanceUsd",
                table: "strategy_models",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Venues",
                table: "strategy_models",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "strategy_deployments",
                columns: table => new
                {
                    AccountKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ModelId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_strategy_deployments", x => x.AccountKey);
                    table.ForeignKey(
                        name: "FK_strategy_deployments_strategy_models_ModelId",
                        column: x => x.ModelId,
                        principalTable: "strategy_models",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_strategy_deployments_ModelId",
                table: "strategy_deployments",
                column: "ModelId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "strategy_deployments");

            migrationBuilder.DropColumn(
                name: "AccountType",
                table: "strategy_models");

            migrationBuilder.DropColumn(
                name: "MinBalanceUsd",
                table: "strategy_models");

            migrationBuilder.DropColumn(
                name: "Venues",
                table: "strategy_models");
        }
    }
}
