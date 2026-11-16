using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class ReshapeModelDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_model_decisions_InstanceId_BarTs",
                table: "model_decisions");

            migrationBuilder.DropColumn(
                name: "BarTs",
                table: "model_decisions");

            migrationBuilder.DropColumn(
                name: "Fired",
                table: "model_decisions");

            migrationBuilder.DropColumn(
                name: "InputJson",
                table: "model_decisions");

            migrationBuilder.DropColumn(
                name: "LatencyUs",
                table: "model_decisions");

            migrationBuilder.DropColumn(
                name: "PackageId",
                table: "model_decisions");

            migrationBuilder.DropColumn(
                name: "Step",
                table: "model_decisions");

            migrationBuilder.DropColumn(
                name: "TargetsJson",
                table: "model_decisions");

            migrationBuilder.RenameColumn(
                name: "InstanceId",
                table: "model_decisions",
                newName: "Symbol");

            migrationBuilder.AlterColumn<string>(
                name: "BookJson",
                table: "model_decisions",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Account",
                table: "model_decisions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "EventTs",
                table: "model_decisions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Strategy",
                table: "model_decisions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "VenueId",
                table: "model_decisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VenueKind",
                table: "model_decisions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_model_decisions_Account_EventTs",
                table: "model_decisions",
                columns: new[] { "Account", "EventTs" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_model_decisions_Account_EventTs",
                table: "model_decisions");

            migrationBuilder.DropColumn(
                name: "Account",
                table: "model_decisions");

            migrationBuilder.DropColumn(
                name: "EventTs",
                table: "model_decisions");

            migrationBuilder.DropColumn(
                name: "Strategy",
                table: "model_decisions");

            migrationBuilder.DropColumn(
                name: "VenueId",
                table: "model_decisions");

            migrationBuilder.DropColumn(
                name: "VenueKind",
                table: "model_decisions");

            migrationBuilder.RenameColumn(
                name: "Symbol",
                table: "model_decisions",
                newName: "InstanceId");

            migrationBuilder.AlterColumn<string>(
                name: "BookJson",
                table: "model_decisions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<long>(
                name: "BarTs",
                table: "model_decisions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "Fired",
                table: "model_decisions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "InputJson",
                table: "model_decisions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "LatencyUs",
                table: "model_decisions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PackageId",
                table: "model_decisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "Step",
                table: "model_decisions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "TargetsJson",
                table: "model_decisions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_model_decisions_InstanceId_BarTs",
                table: "model_decisions",
                columns: new[] { "InstanceId", "BarTs" },
                unique: true);
        }
    }
}
