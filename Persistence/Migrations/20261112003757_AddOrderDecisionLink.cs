using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderDecisionLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DecisionId",
                table: "orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_DecisionId",
                table: "orders",
                column: "DecisionId");

            migrationBuilder.AddForeignKey(
                name: "FK_orders_model_decisions_DecisionId",
                table: "orders",
                column: "DecisionId",
                principalTable: "model_decisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_orders_model_decisions_DecisionId",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_DecisionId",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DecisionId",
                table: "orders");
        }
    }
}
