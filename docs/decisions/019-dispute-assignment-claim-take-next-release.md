# 019. Dispute assignment: claim, take next, release

Status: accepted (2026-11-16). Amends [ADR-003](003-dispute-state-machine.md) (a new lifecycle row and an
assignee-only guard), [ADR-007](007-http-status-policy-and-idempotency.md) (409 `not_assignee` in the check order, 204
from take next) and [ADR-014](014-advisory-lock-on-dispute-caps.md) (take next takes a row lock; other agent moves still
take none).

## Context
Any agent could accept or reject any dispute under review, including one a colleague was halfway through. The xmin
ETag stopped two agents from both writing the same version, but not from deciding each other's work, and nothing
told an agent which disputes were theirs. Agents also had to pick from the queue by hand, so at a busy moment several
would open the same oldest dispute and all but one would get 412.

## Decision
1. **The assignee is a column**, `disputes.assignee_id varchar(64) NULL` (the length of `actor_id`). Rejected: deriving
   it from `dispute_status_changes`. A per-row lookup would defeat the keyset index on the queue, the aggregate should
   guard on its own field, and a column can carry a database backstop later.
2. **The aggregate owns it.** `Dispute.Transition` sets `AssigneeId` to the acting agent when the dispute enters
   UnderReview and clears it on every way out: accept, reject, release, and the customer's withdrawal, which goes
   through the same method.
3. **Release** is a new lifecycle row, `(UnderReview -> Submitted, Agent)`, recorded in the history like any move. It
   takes no note: notes are shown to the customer, and a reason for handing a dispute back is the agent's own business.
   The validator answers a note with 400 `validation_failed` under `errors.note`; the Domain also refuses one
   (`note_not_allowed`) for callers that skip the validator. A blank note counts as none in both.
4. **Only the assignee may accept, reject or release**, enforced in the aggregate:
   `DisputeLifecycle.IsAssigneeOnly(from, actor)` holds for an agent's move out of UnderReview, and `Transition` then
   throws `NotAssigneeException` (`not_assignee`, "Another agent is reviewing this dispute.", no identity) when the
   assignee is someone else. It maps to **409**: the request conflicts with the dispute's current state. The check order
   on `POST /agent/disputes/{id}/transitions` is 404, 428 (no If-Match), 412 (stale), 409 `invalid_transition`, 409
   `not_assignee`, then the write (an xmin conflict there is still 412). A null assignee is tolerated for now (see
   Rollout). Each `not_assignee` refusal is logged (`agent.dispute.denied`, event 5105, with the dispute id and the
   refused agent's subject only, never the reviewing agent) and counted (`audit.events{action=agent_dispute_denied}`).
5. **Migration `AgentAssignment`** adds the column, assigns each UnderReview dispute to the agent of its latest move to
   UnderReview, adds the partial index `ix_disputes_assignee_queue (assignee_id, created_at, id) WHERE status =
   'UnderReview'`, and grants `UPDATE (assignee_id) ON disputes` to `dispute_app`. No CHECK constraint yet. The backfill
   rewrites those rows, so their xmin (ETag) changes: an agent holding an old ETag gets 412 and reloads. It first runs
   `SET LOCAL lock_timeout = '5s'` inside its own transaction (EF wraps each migration in one; a plain `SET` would stay
   on the session), so its ACCESS EXCLUSIVE lock gives up with 55P03 behind a long-open transaction instead of queueing
   every disputes query behind itself; the migrate run is retried (runbook, Assignment rollout order).
6. **Take next**, `POST /api/v1/agent/disputes/take-next` (no body, no If-Match). One `IUnitOfWork.SaveInTransactionAsync`
   attempt runs `SELECT id FROM disputes WHERE status = 'Submitted' ORDER BY created_at, id LIMIT 1 FOR UPDATE SKIP LOCKED`,
   loads that aggregate, applies `Transition(UnderReview, agent)`, saves, reads the detail and commits. The answer is
   200 with the same body and ETag as the detail, or **204** when nothing is unclaimed (an empty pool is not an error or
   a conflict). The detail is read inside the transaction, after the save and while the row lock is still held (one
   more query under the lock), so the answer is the claim itself: nothing can land between the claim and the response,
   and the xmin it carries is this transaction's, which the commit does not change. It is a projection read, not built
   from the aggregate, because the body needs the customer, account and transaction. A claim that cannot be read back
   rolls back with it. It is audited and logged like a start review (`agent.dispute.transitioned`, event 5102, ids
   only). A plain start review or a customer withdrawal racing it blocks on its UPDATE until take next commits, then
   fails the xmin check: 412 (both tested).
   - Rejected: pick the oldest without a lock and retry on an xmin conflict. Every agent contends for the same row, and
     the retries run out exactly when the queue is busiest.
   - Rejected: one SQL `UPDATE ... RETURNING`. It bypasses the aggregate (ADR-003) and duplicates the lifecycle in SQL.
7. **Picking one** stays `POST /{id}/transitions` with `UnderReview`, which also assigns.
8. **Queue filter** `?assignment=unclaimed|mine|others` (any case). Unclaimed is Submitted; mine is UnderReview with the
   caller as assignee; others is UnderReview with any other assignee or none (`IS DISTINCT FROM`). Combined with
   `status` it is 400 on `assignment`. The order stays `(created_at, id)`, so the cursor format is unchanged. The cursor
   fingerprint without an assignment is byte-identical to before (`agent-queue;status=...`), so cursors issued before
   the deploy stay valid; with one it is `agent-queue;assignment=<value>`. "mine" is per agent through the cursor's
   subject scope.
9. **Exposure.** Agents get `assignment` (relative to the caller: `Unclaimed`, `You`, `AnotherAgent`, null once
   decided or withdrawn), `assigneeId` (the full subject, the disclosure level agents already have in the timeline's
   `actorId`) and `claimedAt` (`updated_at` while UnderReview; nothing else updates a row under review) on the queue
   items and the detail. `assigneeId` and `claimedAt` are sent only while UnderReview. Customers get none of it: their
   records are unchanged.

## Consequences
- Two agents can no longer decide the same dispute, and concurrent take-next calls never hand out the same one.
- **No claim expiry.** A claim lasts until the agent decides or releases it, or the customer withdraws. An agent who
  leaves holds their disputes; the runbook's break-glass step returns one to the queue. Upgrade path: a lease of one
  shift length, then a `dispute-supervisor` role that can reassign.
- **A lost commit acknowledgement claims twice.** If take next's COMMIT lands but its acknowledgement is lost, the
  execution strategy runs the attempt again and claims a second dispute. The first stays under review by the same agent
  (their "mine") and can be released.
- **FIFO is approximate under contention.** SKIP LOCKED skips a row another take next is holding, so a caller can get the
  second-oldest while the oldest is being claimed (and, if that claim rolls back, the oldest waits for the next call).
- **Claims per agent are unbounded.** Nothing stops one agent from taking many.
- **A released dispute comes straight back.** A release keeps the dispute's `created_at`, so it is at once the oldest
  unclaimed one, and the releaser's next take next can hand it straight back to them. An agent who released a dispute
  on purpose picks a specific one from the queue (start review on it) instead of taking next. Upgrade path: skip
  disputes the caller released in the last few minutes, or order released ones after a cool-down.
- **Assignment gates decisions, not reads.** Any agent can still read any open dispute and its customer's transactions.
- **Customers see a release as a move back to Submitted** on their timeline (actor type Agent), never who. The current
  web words it "Back-office changed the status from Under review to Submitted" and its stepper returns to step 1;
  friendlier wording such as "returned to the review queue" is for the web change that follows.

## Proof
`DisputeLifecycleTests.EveryMoveIsAllowedExactlyWhenItIsInTheTable` (assignee set on entry, cleared on every exit),
`AnotherAgentCannotDecideOrReleaseAndNothingChanges`, `AnUnderReviewDisputeWithNoAssigneeCanBeDecidedByAnyAgentForNow`,
`AReleaseWithANoteIsRefusedAndNothingChanges`; `ApiExceptionHandlerTests.AnAgentWhoIsNotTheAssigneeIs409NotAssignee`;
`AgentCursorProtectorTests.WithoutAnAssignmentTheFingerprintIsTheOneOlderVersionsIssued`;
`AgentCommandsTests.TakeNextAnswers204WhenThereIsNothingToTake`, `TakeNextClaimsTheLockedDisputeAndReadsItBackInsideTheTransaction`,
`AClaimThatCannotBeReadBackIsANotFoundAndRecordsNothing`, `AMoveOnSomeoneElsesReviewIsCountedAsDeniedAndNothingIsSaved`;
`AgentAssignmentTests` (409 on accept, reject and release by another agent with nothing changed, release, withdrawal
clears the assignee, the three filters including a legacy row with no assignee under others, an empty "mine", cursors
per filter, refused callers on `assignment=mine`, the customer's timeline after a release, a release racing the
assignee's own decision, the runbook's break-glass statement read from the runbook);
`AgentDeniedAuditTests` (a refusal is logged and counted with ids only, a success is not);
`AgentTakeNextTests.TwoAgentsTakingNextAtOnceGetDifferentDisputes` and `EightParallelTakeNextsNeverHandOutADisputeTwice`
(each racer holds its row lock at a gate at the same time; without SKIP LOCKED they fail),
`TakeNextAssignsTheOldestUnclaimedAndReturnsItsETag`, `AStartReviewRacingAHeldTakeNextIs412`,
`ACustomerWithdrawalRacingTakeNextIs412AndTheAnswerIsTheClaim`, refused callers changing nothing;
`AgentAssignmentMigrationTests` (lock_timeout set locally inside the migration's transaction; behind a held lock it
fails with 55P03, rolls back, leaves no session setting and succeeds on the retry);
`DatabasePrivilegeTests.TheAppRoleCanClaimAndReleaseADispute`, `TwoAppSessionsTakingNextSkipEachOthersLockedRow`
(the repository's own SQL, two sessions, the second gets the next row without waiting; column grants);
`DisputeSecurityTests.TheCustomerViewNeverCarriesTheAssignee`; e2e `assignment.spec.ts`.

## Rollout (expand, then contract)
- **Deploy order: web first, then api.** Until the Phase 5 web ships, a claim made with Start review cannot be released
  from the UI (only through the API or the runbook's break-glass step), and agents who are not the assignee still see
  Accept and Reject and get 409 `not_assignee`. The api bump for this change is therefore held until the Phase 5 web
  has rolled out; that web tolerates an older api (the new fields are additive and it copes without them). This is an
  exception to the runbook's "api first when the OpenAPI document changed", which assumes the web needs the new api.
- **Now (expand).** The column is nullable with no CHECK. Pods from before this change keep working during a rolling
  deploy, but they ignore the column: a dispute one of them puts under review has no assignee (any agent may decide
  it, which is why the guard tolerates null), and an accept, reject or customer withdrawal of a claimed dispute served
  by one of them leaves a stale non-null assignee on a row that is no longer UnderReview. The projections show an
  assignee only while UnderReview, so neither shows, but both violate `(status = 'UnderReview') = (assignee_id IS NOT
  NULL)`. Old web clients ignore the new fields.
- **Contract (a later change), only after the Phase 4 api has fully rolled out** (no old pod serving, including after a
  rollback). In order: backfill the assignee of UnderReview rows that have none; clear `assignee_id` on rows that are
  not UnderReview; `ADD CONSTRAINT ck_disputes_assignee CHECK ((status = 'UnderReview') = (assignee_id IS NOT NULL))
  NOT VALID`; `VALIDATE CONSTRAINT ck_disputes_assignee`; drop the null tolerance in `Dispute.Transition`. With an old
  pod still serving after the CHECK, every accept, reject or withdrawal of a claimed dispute and every start review it
  served would fail. After that step, rolling the api back past this change needs a forward fix, for the same reason.
