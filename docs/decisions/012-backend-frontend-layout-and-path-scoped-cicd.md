# 012. Backend and frontend folders; path-scoped CI/CD to follow

Status: accepted (2026-11-05). Amends [ADR-009](009-local-and-ci-deploy.md).

## Context
The backend (`src/`, `tests/`) and the SPA (`web/`) sat beside CI, compose, infra and docs at the repository root.
The goal is for a backend-only change to build and publish only the API image, and a frontend-only change only the
web image, without splitting the repository.

## Decision
- The two halves become folders: `backend/src`, `backend/tests` and `frontend` (was `web`).
- The root stays the orchestration layer and does not move: `DisputePortal.slnx`, `global.json`,
  `Directory.Build.props`, `.editorconfig`, `Dockerfile` (multi-target: the API by default, `--target web`),
  `docker-compose.yml`, `deploy/`, `e2e/`, `scripts/`, `docs/`. So `docker build .`, `docker compose up --build` and
  `dotnet test` from the root work as before (the one-root-Dockerfile decision of the original plan stands).
- `docs/openapi.json` is the contract between the halves. `backend/tests/DisputePortal.UnitTests` also reads
  `frontend/src/lib/validation-cases.json`, so a change to either file concerns both halves.
- Path-scoped CI and per-half publish are a separate change (one workflow keeps the required `ci-ok` check; a
  `changes` job gates each half). This ADR only moves the folders and rewrites the paths that referred to them.

## Consequences
- No behaviour change: image contents, tags and the OpenAPI snapshot are unchanged.
- `RepositoryFiles.ApiSources()` reads `backend/src/DisputePortal.Api` and reports paths relative to `backend/`, so
  the architecture-test path rules still read `src/DisputePortal.Api/...`.
- Historical evidence (`docs/mutation-checks.md`, `docs/design/`, `specs/`) keeps the old `src/`, `tests/` and
  `web/` paths. Map them as `src` → `backend/src`, `tests` → `backend/tests`, `web` → `frontend`.
- A fully self-contained `backend/` (own Dockerfile and solution) was rejected: it would break `docker build .` and
  root-level `dotnet` for graders and contradict the one-root-Dockerfile decision.
