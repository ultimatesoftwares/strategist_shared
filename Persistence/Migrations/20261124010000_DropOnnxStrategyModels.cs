using Microsoft.EntityFrameworkCore.Migrations;

namespace Server.Data.Migrations;

/// <summary>
/// ONNX support was dropped: the model registry is ML.NET (.zip) only, and
/// <c>ModelStorage</c> no longer resolves <c>.onnx</c> blobs, so legacy rows
/// can never load again. Purge them (and their deployments / project links)
/// rather than leave rows that 500 on every backtest. Orphaned .onnx blobs on
/// disk are left behind; they are inert and can be deleted by hand.
/// </summary>
public partial class DropOnnxStrategyModels : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DELETE FROM strategy_deployments d
            USING strategy_models m
            WHERE d."ModelId" = m."Id" AND m."StoragePath" LIKE '%.onnx';
        DELETE FROM project_strategy_links l
            USING strategy_models m
            WHERE l."ModelId" = m."Id" AND m."StoragePath" LIKE '%.onnx';
        DELETE FROM strategy_models WHERE "StoragePath" LIKE '%.onnx';
        """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data purge is irreversible; nothing to restore.
    }
}
