using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DisputePortal.Api.Shared.Persistence.Migrations
{
    /// <summary>
    /// ADR-019, the contract step: a dispute has an assignee exactly while it is UnderReview, enforced by
    /// <c>ck_disputes_assignee</c>.
    /// <para>
    /// THIS MIGRATION MUST NOT BE APPLIED UNTIL NO API POD OLDER THAN THE ASSIGNMENT RELEASE (PR 59, migration
    /// AgentAssignment) IS SERVING, including after a rollback. Those pods ignore assignee_id: their start review leaves
    /// it null and their accept, reject or withdrawal of a claimed dispute leaves it set, so every such write would fail
    /// this CHECK (23514, a 500). The deploy order is held by people, through the homelab bump PRs. Check it first: every
    /// running dispute-api pod's image digest must be sha256:455404513e37... (the api bump 73934af in homelab-infra, a
    /// build of PR 59's merge commit 91e6f60, the first that qualifies) or a later api bump (runbook, "Contract").
    /// </para>
    /// <para>
    /// One transaction (EF wraps each migration in one): if anything fails, nothing of it stays. The CHECK is added
    /// validated, not NOT VALID then VALIDATE: ADD CONSTRAINT takes ACCESS EXCLUSIVE and a lock is held until the
    /// transaction ends, so a VALIDATE in the same transaction would scan under that same lock and gain nothing, while
    /// splitting it out of the transaction would lose SET LOCAL lock_timeout and the all-or-nothing rollback. The cost is
    /// one scan of disputes under the lock, small at this table's size.
    /// </para>
    /// </summary>
    public partial class AgentAssignmentInvariant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // As in AgentAssignment: a lock wait gives up after 5 s (55P03), so disputes queries queue behind this
            // migration for up to 5 s per attempt, not indefinitely; it then rolls back whole and is run again (EF's
            // retries, the migrate Job, the sync). SET LOCAL ends with this transaction.
            migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");

            // The ADD CONSTRAINT below needs ACCESS EXCLUSIVE. Taken first, so no write lands between the clean-up and the
            // CHECK, and the UPDATEs' ROW EXCLUSIVE never has to be upgraded while an app transaction waits on one of
            // their row locks (a deadlock). While LOCK TABLE waits, every new query on disputes from the live pods (reads
            // included) queues behind it: up to 5 s per attempt, again on every retry. Once held, it lasts two UPDATEs and
            // one scan. The API's own statement_timeout is also 5 s, so a query queued for nearly the whole wait, plus the
            // hold, can be cancelled (57014) and that request fails; the 10 s command timeout is not the binding limit.
            migrationBuilder.Sql("LOCK TABLE disputes IN ACCESS EXCLUSIVE MODE;");

            // Disputes still under review with no assignee (put there by a pod older than the column) go to the agent who
            // last moved them there: the same rule and tie-break as AgentAssignment. A no-op when there are none.
            migrationBuilder.Sql("""
                UPDATE disputes d
                SET assignee_id = (
                    SELECT c.actor_id FROM dispute_status_changes c
                    WHERE c.dispute_id = d.id AND c.to_status = 'UnderReview'
                    ORDER BY c.occurred_at DESC, c.id DESC
                    LIMIT 1)
                WHERE d.status = 'UnderReview' AND d.assignee_id IS NULL;
                """);

            // Stale assignees an old pod left on disputes it decided, or on withdrawals it served. Not shown anywhere (the
            // projections send an assignee only while UnderReview), so clearing them changes nothing an agent sees.
            migrationBuilder.Sql("""
                UPDATE disputes SET assignee_id = NULL
                WHERE status <> 'UnderReview' AND assignee_id IS NOT NULL;
                """);

            // An UnderReview dispute with no move to UnderReview in its history has nobody to assign: do not invent one.
            // Abort (P0001, not retried) and roll everything above back; the message carries a count, never an id.
            migrationBuilder.Sql("""
                DO $$
                DECLARE unassigned bigint;
                BEGIN
                    SELECT count(*) INTO unassigned FROM disputes WHERE status = 'UnderReview' AND assignee_id IS NULL;
                    IF unassigned > 0 THEN
                        RAISE EXCEPTION 'AgentAssignmentInvariant aborted, nothing changed: % dispute(s) are UnderReview with no assignee and no move to UnderReview in their history. Return them to the queue first: docs/runbook.md, "The AgentAssignmentInvariant migration aborted".', unassigned;
                    END IF;
                END
                $$;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_disputes_assignee",
                table: "disputes",
                sql: "(status = 'UnderReview') = (assignee_id IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_disputes_assignee",
                table: "disputes");
        }
    }
}
