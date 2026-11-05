# 012. Backend and frontend folders; path-scoped CI/CD to follow

Status: accepted (2026-11-05). Amends [ADR-009](009-local-and-ci-deploy.md): "deploy" is still images published to GHCR, now per half.

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
- CI and publish are path-scoped inside the one `ci.yml`, so `ci-ok` stays the single required check (a skipped job
  passes it; a failed or cancelled job, or a `changes` job that did not succeed, fails it). A `changes` job runs
  `scripts/changed-paths.sh` over the changed files and outputs `backend`, `frontend`, `stack` (build and scan both
  images and run the E2E) and the halves whose image content changed. Backend and frontend jobs run only for their
  half; `images` and `e2e` run when the stack is touched; `publish` is a matrix with one leg per changed image, so a
  backend change publishes `dispute-portal-api` only and a frontend change `dispute-portal-web` only. A change to
  docs, specs or the README builds and publishes nothing. The classifier fails closed: if the diff cannot be computed
  or a path looks quoted, everything runs, and a path no rule knows runs the verification jobs. On a push the diff is
  taken against the last commit whose `ci` run on the default branch succeeded, not against the previous commit, so a
  failed run or a queued run GitHub replaced cannot lose an image publish. Paths come from `scripts/changed-files.sh`
  (NUL-delimited, no rename detection) because a plain `git diff --name-only` C-quotes non-ASCII paths and lists only
  the new path of a rename. `publish` gates on explicit job results (`!cancelled()`), because the implicit `success()`
  is false whenever any upstream job was skipped. `scripts/test-changed-paths.sh` is the table test and runs in the `changes` job.
- The two shared inputs are classified explicitly: `frontend/src/lib/validation-cases.json` is read by the backend
  unit tests, so it triggers both halves; `docs/openapi.json` makes both the backend and the frontend job run (the
  snapshot test and the type generation check it) without publishing an image.
- A manual run (`workflow_dispatch`, input `half`) builds and verifies both halves and is a dry run unless
  `dry_run` is false, and it only pushes from `main`.

## Consequences
- No behaviour change to the images themselves: contents and the OpenAPI snapshot are unchanged. Tags are now per image: a commit has `:<sha>` only for the image it changed, so a rollback or deploy pins each image to its own last published sha or digest (runbook).
- `RepositoryFiles.ApiSources()` reads `backend/src/DisputePortal.Api` and reports paths relative to `backend/`, so
  the architecture-test path rules still read `src/DisputePortal.Api/...`.
- Historical evidence (`docs/mutation-checks.md`, `docs/design/`, `specs/`) keeps the old `src/`, `tests/` and
  `web/` paths. Map them as `src` → `backend/src`, `tests` → `backend/tests`, `web` → `frontend`.
- A fully self-contained `backend/` (own Dockerfile and solution) was rejected: it would break `docker build .` and
  root-level `dotnet` for graders and contradict the one-root-Dockerfile decision.
