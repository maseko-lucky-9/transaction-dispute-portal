# Implementation Plan: Transaction Disputes

**Spec**: [spec.md](spec.md) · **Constitution check**: pass (see below) · **Authoritative detail**: `docs/design/transaction-dispute-portal-system-design.md` and `docs/decisions/`.

## Technical context
.NET 10 / C# 14, ASP.NET Core Minimal API, EF Core 10 + Npgsql (snake_case), FluentValidation 12, PostgreSQL 17,
Redis 8, Keycloak 26, Vault 1.20, nginx-unprivileged; React 19 + Vite + TS strict; xUnit 2.9.3, Testcontainers,
Playwright. Target: Linux containers, amd64 and arm64.

## Constitution check
| Principle | How it is met |
|---|---|
| I fail-closed ownership | `Owner` named filter, architecture test, BOLA 404 tests |
| II DB enforces concurrency | partial unique index, `xmin`/ETag, grants test as `dispute_app` |
| III lowest provable layer | Domain unit tests; HTTP + Postgres integration tests; Playwright for journeys |
| IV one error contract | RFC 9457 + `code` + `traceId`; ADR-007 |
| V no secrets in source | Vault AppRole, gitleaks hook + CI |

## Structure
See the repository layout in `CLAUDE.md` and ADR-001. Slices: Transactions, Disputes, Agent, Bff.

## Delivery
Eight PRs (walking skeleton first, because auth/Keycloak/Vault are the riskiest): PR1 skeleton, PR2 spec and design,
PR3 transactions, PR4 disputes, PR5 agent, PR6 ops hardening, PR7 final docs. The cut line (screenshots, Stryker gate,
agent UI polish) is decided on 2026-11-08.
