using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisputePortal.Api.Shared.Persistence.Migrations
{
    /// <summary>
    /// ADR-019, the expand step: who is reviewing a dispute. Old pods ignore the column, so it is nullable and has no
    /// CHECK yet; the contract step (after every api pod runs this code) backfills again and adds
    /// <c>CHECK ((status = 'UnderReview') = (assignee_id IS NOT NULL))</c>.
    /// </summary>
    public partial class AgentAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADD COLUMN takes ACCESS EXCLUSIVE on disputes. Behind a transaction that holds any lock on the table (a
            // submission left idle in its transaction, ADR-014) it would wait, and every query on disputes would queue
            // behind it. Give up after 5 s instead (55P03): the migration rolls back whole and the migrate Job runs it
            // again (runbook). SET LOCAL lasts until this migration's transaction ends; a plain SET would stay on the
            // pooled session. It also bounds the waits of the backfill, the index build and the grant below.
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");

            migrationBuilder.AddColumn<string>(
                name: "assignee_id",
                table: "disputes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // Each dispute already under review is assigned to the agent who last moved it there. This rewrites those
            // rows, so their xmin (the ETag) changes: an agent holding an old ETag gets 412 and reloads.
            migrationBuilder.Sql("""
                UPDATE disputes d
                SET assignee_id = (
                    SELECT c.actor_id FROM dispute_status_changes c
                    WHERE c.dispute_id = d.id AND c.to_status = 'UnderReview'
                    ORDER BY c.occurred_at DESC, c.id DESC
                    LIMIT 1)
                WHERE d.status = 'UnderReview';
                """);

            migrationBuilder.CreateIndex(
                name: "ix_disputes_assignee_queue",
                table: "disputes",
                columns: new[] { "assignee_id", "created_at", "id" },
                filter: "status = 'UnderReview'");

            // Claiming, releasing and every exit from UnderReview write the assignee; the app role still cannot change a
            // dispute's identity, owner, subject or amount.
            migrationBuilder.Sql("GRANT UPDATE (assignee_id) ON disputes TO dispute_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("REVOKE UPDATE (assignee_id) ON disputes FROM dispute_app;");

            migrationBuilder.DropIndex(
                name: "ix_disputes_assignee_queue",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "assignee_id",
                table: "disputes");
        }
    }
}
