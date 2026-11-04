# Feature Specification: Transaction Disputes

**Branch**: `001-transaction-disputes` · **Created**: 2026-11-04 · **Status**: Approved (plan rev 2)
**Input**: "A front end and back end where customers view their transactions and dispute them, with a historic view of
disputed transactions. Production-grade, runnable Dockerfile, README with build, run and test."

## User scenarios

### US1 — View my transactions (P1)
A signed-in customer sees a paged list of their own transactions, newest first, filterable by date range and status.
Each row shows whether it is disputable and, if a dispute exists, its status.
**Acceptance**
1. Given I have 150 transactions, when I open the list, then I see the newest page and a way to load the next, with no
   duplicates or gaps across pages.
2. Given another customer's transaction id, when I request it, then I get 404 (never 403).
3. Given a pending, credit or older-than-120-days transaction, then it is shown as not disputable with the reason.

### US2 — Dispute a transaction (P1)
I choose a reason, describe the problem, optionally dispute a partial amount, and submit.
**Acceptance**
1. Given an eligible transaction, when I submit, then I get 201 with the dispute (status Submitted) and it appears in
   my history.
2. Given an ineligible transaction, then I get 422 listing every violated rule at once.
3. Given an active dispute already exists for the transaction, then I get 409 with a link to it.
4. Given I retry the same submission (same id) after a timeout, then I get the original dispute back, not a duplicate.
5. Given a description containing a card or SA ID number, then it is rejected (400 `sensitive_data`) before anything is stored.
6. Given an invalid form, then the UI shows inline errors and sends no request.

### US3 — Dispute history (P1)
I see every dispute I have raised, newest first, across all statuses, and open one to see its timeline.
**Acceptance**: the timeline shows each status change with time and actor type (never an agent's identity).

### US4 — Withdraw a dispute (P2)
I can withdraw a Submitted or UnderReview dispute.
**Acceptance**: stale view returns 412; missing `If-Match` returns 428; a terminal dispute returns 409.

### US5 — Agent review (P2)
A back-office agent sees a queue across customers, opens a dispute, and moves it Submitted → UnderReview → Accepted or
Rejected with a note. The customer sees the resolution.
**Acceptance**: an agent cannot act on a customer's behalf; a customer cannot reach `/agent`; a session with both roles
is refused; every view and transition is attributed and logged.

## Requirements

### Functional
- FR-1 List/get own transactions with keyset paging, filters and `isDisputable`.
- FR-2 Submit a dispute with the eligibility chain (posted, debit, within 120 days, amount ≤ transaction, ≤ 20 active per customer, ≤ 3 per transaction).
- FR-3 Dispute history list and detail with an append-only timeline.
- FR-4 Withdraw (customer) and transitions (agent) through a state machine.
- FR-5 Sign in/out through Keycloak; roles `customer` and `dispute-agent`.
- FR-6 Reason catalogue endpoint.

### Non-functional
- NFR-1 Security: BFF sessions (15 min idle/8 h absolute), no tokens in JS, fail-closed ownership, CSRF header, CSP, least-privilege DB role, Vault-only credentials.
- NFR-2 Scalability: stateless API (`--scale api=3`); keyset indexes; capacity assumptions in the design doc.
- NFR-3 Reliability: idempotent submit, optimistic concurrency, readiness checks, graceful shutdown, bounded startup retry.
- NFR-4 Observability: OpenTelemetry traces/metrics, JSON logs with trace id, domain and security-event counters, request ids from nginx.
- NFR-5 SLOs (assumed): 99.9% availability; p95 ≤ 300 ms reads, ≤ 500 ms writes.
- NFR-6 Operability: one-command start on a clean clone; CI runs the full stack E2E.
- NFR-7 Privacy (POPIA): masked account numbers, no PII in logs, agent views logged, retention in the runbook.

## Out of scope (README "Next")
Attachments, notifications, live cloud deploy, outbox/event publishing, back-channel logout, agent TOTP, dynamic DB
credentials, a distributed rate limiter.

## Success criteria
SC-1 A reviewer runs `docker compose up --build` and completes US1–US5 with the demo users.
SC-2 11 of 11 grading criteria are evidenced in the README by a test or artifact.
SC-3 CI is green: build, unit, integration, frontend, image scan, and Playwright E2E.
