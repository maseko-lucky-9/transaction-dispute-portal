# 007. HTTP status policy, idempotent submit and optimistic concurrency

Status: accepted (2026-11-04)

## Decision
- 400 malformed or failing validation; 401 unauthenticated; 403 forbidden, CSRF failure or role conflict;
  404 missing or not owned (BOLA looks the same as absent); **409 conflicts with the resource's current state**
  (a dispute already exists, an invalid transition, an `xmin` conflict); 412/428 precondition failed/required;
  **422 business-rule violations** (eligibility chain, all at once); 429 with Retry-After.
- `POST /disputes` takes a client-generated UUID as the dispute id. A retry of the same id returns 200 with the
  dispute as it is now (current state and ETag, not a stored copy of the first response) instead of 409, and EF's retry strategy cannot double-insert. The 409 `dispute_exists` body carries
  `existingDisputeId`.
- Dispute reads return an `ETag` from Postgres `xmin`. Withdraw and agent transitions require `If-Match` (428 if
  missing, 412 if stale). A partial unique index on active disputes is the database backstop.
- Errors use one RFC 9457 envelope with `code` and `traceId`.

### Refinement (PR4): the exact mappings
- **Submit, checked in this order.**
  1. Shape: 400 `validation_failed` with `errors{}`. A card or ID number in the description is also 400, with code
     `sensitive_data` (spec US2.5), and nothing is stored.
  2. Transaction missing or not owned: 404.
  3. Same id, same customer:
     - same `transactionId`: 200 with the dispute as it is now, with its current ETag (no new row, no second history
       entry). If the client edited the text between attempts, the first text is kept and the form says so;
     - another transaction: 409 `idempotency_key_reuse`.
     The replay compares the transaction only; the client reuses an id only for a retry of the same form.
  4. An active, Accepted or Rejected dispute on the transaction: 409 `dispute_exists` with `existingDisputeId`.
  5. Eligibility: 422 `eligibility_failed` with every violation.
  6. Insert: 201 with `Location` and `ETag`.
- **An id that belongs to another customer** reveals no content. The insert collides on the primary key, the re-read
  under the Owner filter finds nothing, and the answer is a generic 409 `dispute_id_conflict`, never 200.
  Accepted residual leak: 409 versus 201 tells a caller that the id exists. The id is a random 122-bit value only its
  owner sees and submits are limited to 10 a minute per user, so it cannot be probed. Scoping the key per customer
  (for example a UUIDv5 over customer and client id) removes it.
- **Races, decided by the database.**
  - The same id racing itself: the loser collides on the primary key, re-reads and replays (201 and 200).
  - Different ids on one transaction: the loser hits `ux_disputes_one_active_per_transaction` and gets 409 `dispute_exists`.
  - EF's retrying execution strategy re-running a SaveChanges whose COMMIT landed but whose acknowledgement was lost
    collides on the primary key and replays (200). No explicit execution-strategy block is needed, because the
    client-generated id is the idempotency key.
- **Known limit: withdraw has no replay.** If a withdraw's COMMIT lands but its acknowledgement is lost and the retry
  re-runs the UPDATE guarded by `xmin`, it matches no row and the user sees 412 "changed, reload" for their own
  successful withdraw. Reloading shows Withdrawn.
- **Withdraw (and agent transitions in PR5), checked in this order.**
  1. Ownership: 404.
  2. `If-Match` missing: 428 `precondition_required`.
  3. Tag stale: 412 `precondition_failed` with the current `ETag`. A stale tag wins over a terminal state, because every
     change also changes the tag.
  4. Fresh tag but the move is not in the lifecycle table: 409 `invalid_transition`.
  5. A change that lands between the read and the write fails the `xmin` check (`DbUpdateConcurrencyException`). That is
     a precondition that failed at commit time, so it is also 412 with the current `ETag`, not 409.
  Only a strong tag matches. Weak tags and `*` are 412: a state change must name the version it was decided on.
- **Who writes the 412.** The endpoint writes it, not the exception handler, because `ExceptionHandlerMiddleware` strips
  `ETag` (with the other cache headers) from every response it writes.

## Consequences
Double submits, network retries and stale agent screens are all safe. Tests force the interleaving rather than
relying on timing.
