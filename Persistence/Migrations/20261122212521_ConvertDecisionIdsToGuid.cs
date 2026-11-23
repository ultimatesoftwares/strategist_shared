using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConvertDecisionIdsToGuid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE model_decisions, orders IN ACCESS EXCLUSIVE MODE;
                CREATE TEMPORARY TABLE decision_id_mapping
                    ("OldId" bigint PRIMARY KEY, "NewId" uuid NOT NULL UNIQUE) ON COMMIT DROP;
                INSERT INTO decision_id_mapping ("OldId", "NewId")
                    SELECT "Id", gen_random_uuid() FROM model_decisions;
                ALTER TABLE model_decisions ADD COLUMN "NewId" uuid;
                ALTER TABLE orders ADD COLUMN "NewDecisionId" uuid;
                UPDATE model_decisions d SET "NewId" = m."NewId"
                    FROM decision_id_mapping m WHERE d."Id" = m."OldId";
                UPDATE orders o SET "NewDecisionId" = m."NewId"
                    FROM decision_id_mapping m WHERE o."DecisionId" = m."OldId";
                ALTER TABLE orders DROP CONSTRAINT "FK_orders_model_decisions_DecisionId";
                DROP INDEX "IX_orders_DecisionId";
                ALTER TABLE model_decisions DROP CONSTRAINT "PK_model_decisions";
                ALTER TABLE orders DROP COLUMN "DecisionId";
                ALTER TABLE model_decisions DROP COLUMN "Id";
                ALTER TABLE model_decisions RENAME COLUMN "NewId" TO "Id";
                ALTER TABLE orders RENAME COLUMN "NewDecisionId" TO "DecisionId";
                ALTER TABLE model_decisions ALTER COLUMN "Id" SET NOT NULL;
                ALTER TABLE model_decisions ADD CONSTRAINT "PK_model_decisions" PRIMARY KEY ("Id");
                CREATE INDEX "IX_orders_DecisionId" ON orders ("DecisionId");
                ALTER TABLE orders ADD CONSTRAINT "FK_orders_model_decisions_DecisionId"
                    FOREIGN KEY ("DecisionId") REFERENCES model_decisions ("Id") ON DELETE SET NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Decision GUIDs cannot be converted back to their original numeric IDs.");
        }
    }
}
