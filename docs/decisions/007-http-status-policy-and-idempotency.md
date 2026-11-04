# 007. HTTP status policy, idempotent submit and optimistic concurrency

Status: accepted (2026-11-04)

## Decision
- 400 malformed or failing validation; 401 unauthenticated; 403 forbidden, CSRF failure or role conflict;
  404 missing or not owned (BOLA looks the same as absent); **409 conflicts with the resource's current state**
  (a dispute already exists, an invalid transition, an `xmin` conflict); 412/428 precondition failed/required;
  **422 business-rule violations** (eligibility chain, all at once); 429 with Retry-After.
- `POST /disputes` takes a client-generated UUID as the dispute id. A retry of the same id returns 200 with the
  original body instead of 409, and EF's retry strategy cannot double-insert. The 409 `dispute_exists` body carries
  `existingDisputeId`.
- Dispute reads return an `ETag` from Postgres `xmin`. Withdraw and agent transitions require `If-Match` (428 if
  missing, 412 if stale). A partial unique index on active disputes is the database backstop.
- Errors use one RFC 9457 envelope with `code` and `traceId`.

## Consequences
Double submits, network retries and stale agent screens are all safe. Tests force the interleaving rather than
relying on timing.
