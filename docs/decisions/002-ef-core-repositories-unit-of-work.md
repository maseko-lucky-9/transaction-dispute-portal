# 002. EF Core with aggregate-specific repositories and a Unit of Work

Status: accepted (2026-11-04)

## Context
The brief asks for heavy dependency injection and the repository pattern. SCAD used hand-written SQL; this project
uses EF Core (the owner's instruction), following SCAD's "constrain, don't ban" rule: EF for CRUD and projections,
raw SQL only for what EF cannot express, never interpolated.

## Decision
- `ITransactionRepository`, `IDisputeRepository`, `ICustomerRepository`, one per aggregate root, plus `IUnitOfWork`
  over the DbContext. Repositories expose intent (`GetForCustomerAsync`), not `IQueryable`.
- No generic `IRepository<T>` (it re-exposes the DbContext through a weaker interface).
- Commands load the aggregate through its repository and commit through the Unit of Work. Queries are
  `AsNoTracking` projections to read models (CQRS-lite): reads outnumber writes about 400:1.
- No MediatR: it became commercial in v13 and adds indirection with no benefit here. Endpoint handlers call
  services directly.
- DbContext pooling is off (the Owner query filter reads per-request state).
- `scripts/check-constraints.sh` bans Sqlite, SqlClient, EF InMemory and MediatR; tests run against real Postgres
  (Testcontainers), because InMemory does not enforce the partial unique index or the grants.

## Consequences
Unit tests mock repositories; integration tests prove the SQL. The repository interfaces are the seam for both.
