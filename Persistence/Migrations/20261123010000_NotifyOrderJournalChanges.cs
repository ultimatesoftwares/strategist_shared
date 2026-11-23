using Microsoft.EntityFrameworkCore.Migrations;

namespace Server.Data.Migrations;

public partial class NotifyOrderJournalChanges : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION notify_order_journal_changed() RETURNS trigger AS $$
        BEGIN
            PERFORM pg_notify('strategist_order_journal', '');
            RETURN NULL;
        END;
        $$ LANGUAGE plpgsql;
        CREATE TRIGGER order_journal_changed
            AFTER INSERT OR UPDATE OR DELETE OR TRUNCATE ON orders
            FOR EACH STATEMENT EXECUTE FUNCTION notify_order_journal_changed();
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TRIGGER order_journal_changed ON orders;
        DROP FUNCTION notify_order_journal_changed();
        """);
}
