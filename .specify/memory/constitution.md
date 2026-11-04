# Transactions Dispute Portal Constitution

Adapted from the SCAD constitution (v1.1.0 principles). If a task would violate a principle, the task changes, not
the principle.

## Core principles

### I. Authorization is enforced server-side, in the data layer, and fails closed
Authentication happens at the API boundary; ownership is enforced by a named EF query filter (`Owner`) that returns
nothing when there is no customer in context. Only `Features/Agent` may opt out, and an architecture test enforces
it. The UI reflects permissions; it never enforces them. Another customer's resource is a 404.

### II. The database enforces correctness under concurrency
Invariants that must hold across concurrent requests live in the database: the partial unique index (one active
dispute per transaction), `xmin` optimistic concurrency, CHECK constraints, and least-privilege grants (the app role
cannot UPDATE or DELETE the audit table). Application code may validate early for a better message but never relies
on having checked.

### III. Prove each behaviour at the lowest layer that can prove it
Business rules are unit tests on the Domain. Anything involving SQL, grants, constraints, routing, JSON, status
codes, cookies or auth is an integration test through the real HTTP path against real Postgres and Redis, with the
app connecting as the least-privileged role. A mocked test never stands in for an integration test. Every test is
mutation-checked once: break the code, see red.

### IV. One error contract
Every non-2xx response is an RFC 9457 Problem Details document with a stable machine-readable `code` and a `traceId`.
409 means a conflict with the resource's state; 422 means a business-rule violation (ADR-007).

### V. Secrets never live in source or in the browser
Application credentials come from Vault only. Tokens never reach JavaScript. Only placeholders and documented
local-only demo credentials are committed; gitleaks runs as a hook and in CI.

### VI. Small, correct and genuinely tested beats large and unproven
No feature enters without its proving test. No abstraction enters without a second caller or a test that needs the
seam. A cut line, not heroics, handles a tight week.

### VII. Evidence over assertion
Every claim in the README maps to a test or an artifact in the criteria table. "Done" means pasted command output.

### VIII. One logical change per Conventional Commit, one slice per PR
`type(scope): summary`, subject at most 72 characters. Each PR is one meaningful slice, squash-merged on a green
`ci-ok`. Nobody pushes to `main`.

### IX. Every line explainable; AI use disclosed
An AI assistant drafts; the owner reviews and can defend every line. The README carries an AI-use section.

## Hard constraints

| Constraint | Rule | Gate |
|---|---|---|
| Stack | .NET 10 Minimal API, EF Core 10 + Npgsql, PostgreSQL 17, Keycloak 26, Redis, HashiCorp Vault, nginx, React | Review |
| Banned | Sqlite, SqlClient, EF InMemory, MediatR, Dapper, a generic `IRepository<T>` | `scripts/check-constraints.sh` (hook + CI) |
| Build | Warnings are errors; locked NuGet restore; `npm ci --ignore-scripts` | CI |
| Run | `docker compose up --build` on a clean clone, no `.env`, no make | E2E job + fresh-clone check |
| Database access | App role is DML-only; the audit table is INSERT-only with a DB-stamped time | Integration test connecting as the app role |
| Secrets | gitleaks clean | Hook + CI |

## Governance
This constitution is amended by an ADR in `docs/decisions/`. Version 1.0.0, ratified 2026-11-04.
