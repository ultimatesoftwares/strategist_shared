using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBrokerExecutionTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BrokerAccountKey", table: "orders",
                type: "character varying(256)", maxLength: 256, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "RealizedPnlSource",
                table: "orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "BrokerOrderTicket",
                table: "orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrokerPositionTicket",
                table: "orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_VenueId_BrokerOrderTicket",
                table: "orders",
                columns: new[] { "VenueId", "BrokerOrderTicket" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_VenueId_BrokerPositionTicket",
                table: "orders",
                columns: new[] { "VenueId", "BrokerPositionTicket" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "BrokerAccountKey", table: "orders");
            migrationBuilder.DropColumn(name: "RealizedPnlSource", table: "orders");
            migrationBuilder.DropIndex(
                name: "IX_orders_VenueId_BrokerOrderTicket",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_VenueId_BrokerPositionTicket",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "BrokerOrderTicket",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "BrokerPositionTicket",
                table: "orders");
        }
    }
}
