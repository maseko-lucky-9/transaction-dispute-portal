# 014. Advisory lock on the dispute caps

Status: accepted (2026-11-05). Amends [ADR-007](007-http-status-policy-and-idempotency.md) (races, lost commit
acknowledgement) and [ADR-002](002-ef-core-repositories-unit-of-work.md) (unit of work).

## Context
The caps (20 active disputes per customer, 3 ever per transaction, `DisputePolicyOptions`) were judged on counts read
before an autocommit insert, so concurrent submits by one customer could all read 19 and all be created. Only "one
active dispute per transaction" is a unique index; the counts are not. The reference design: decide in the database
and lock the contended resource, here the customer.

## Decision
- A submit runs one READ COMMITTED transaction whose first statement is
  `pg_advisory_xact_lock(hashtextextended(customer_id::text, 0))`. One key per customer covers both caps. READ
  COMMITTED gives each statement a fresh snapshot, so counts read after a lock wait include the previous holder's
  commit (REPEATABLE READ would keep the snapshot taken at the lock and leave the race open).
- `EnableRetryOnFailure` only allows a user transaction inside `CreateExecutionStrategy().ExecuteAsync`.
  `IUnitOfWork.SaveInTransactionAsync` does that: on a transient failure the whole attempt (lock, reads, insert, commit)
  runs again from an empty change tracker. Refusals are translated after the strategy gives up, so a transient insert
  failure is still retried.
- Inside the lock, in order: the replay check (200 or `idempotency_key_reuse`), the submission facts, `dispute_exists`,
  eligibility, insert. With the replay check before the lock, the second of two identical submits would find nothing,
  wait, then see the first as an existing dispute: 409 instead of 200. Inside it, EF's retry after a lost commit
  acknowledgement is also a plain replay.
- The transaction snapshot and its 404 stay before the lock: the app role cannot write the read model, and a probe of
  someone else's transaction takes no lock.
- A unique violation is resolved (replay, `dispute_exists`, `dispute_id_conflict`) only after the rollback, so the lock
  is not held while resolving and the re-reads do not depend on a failed transaction's state (EF's automatic savepoint
  keeps it usable today; the design does not rely on that).
- The lock sits behind `ICustomerLock` with two adapters: `AdvisoryCustomerLock` in production, which refuses to run
  outside a transaction (the lock would be released at once), and `NoCustomerLock` in the integration tests whose
  purpose is the database backstop: two submits that both pass every check still meet the unique indexes.
- Rejected: `SELECT ... FOR UPDATE` on the customer row needs UPDATE on `customers`, which the app role does not have
  (`DatabasePrivilegeTests`); SERIALIZABLE adds a retry loop and false-positive aborts.

## Consequences
- A customer's submits are serialised: each waits for the previous one's transaction to end. Different customers never
  wait for each other. A lock wait is bounded by the connection's `statement_timeout` (5 s); a wait past it is answered 503
  `database_unavailable` and not retried (`AdvisoryCustomerLock` maps SQLSTATE 57014).
- The unique indexes stay as the backstop: the primary key for idempotency and foreign ids, the partial index for one
  active dispute per transaction.
- Withdrawals and agent transitions take no lock: they never raise either count.
- The per-transaction cap could only be passed with a withdraw landing inside another submit's read-to-insert window;
  both caps are judged on one read under the same lock, so that window is closed too.
- Proof: `DisputeConcurrencyTests` gates each racer after its counts until the others are queued on the lock
  (`AdvisoryLockRendezvous` polls `pg_locks`). Without the lock, or under REPEATABLE READ, all eight parallel submits
  at 19 are created. A transient failure before the commit runs the whole attempt again and creates the dispute once.
- Known limit: nothing on the server bounds how long an *idle* transaction may hold the lock. If the app's connection goes
  half-open between the lock and the commit, that customer's submits answer 503 until the server notices the dead
  connection (TCP keepalive, hours by default). Only that customer is affected. Upgrade path:
  `idle_in_transaction_session_timeout` in the application connection options, set above the 15 s the gated tests hold
  racers idle.
