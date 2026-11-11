# Transactions Dispute Portal

SE3 take-home. Customers view and dispute transactions; agents review disputes. Due 2026-11-10.

- Backend: .NET 10 Minimal API, vertical slices (`backend/src/DisputePortal.Api/Features/*`) + pure `DisputePortal.Domain`. EF Core 10 + Npgsql. No MediatR, no generic repository.
- Auth: Keycloak, API is the OIDC confidential client (BFF). Tokens live encrypted in Redis; the browser holds only the HttpOnly `dp_session` cookie. Credentials come from Vault only (AppRole in compose, Kubernetes auth on a cluster, ADR-016).
- Frontend: `frontend/` React 19 + Vite + TS strict, served by nginx. E2E in `e2e/` (Playwright).
- Run: `docker compose up --build`. Test: see README. CI: `.github/workflows/ci.yml`, required check `ci-ok`; jobs and the image publish are path-scoped per half (ADR-012, `scripts/changed-paths.sh`); after a publish on `main`, `bump-homelab` opens (never merges) a digest-bump PR in `maseko-lucky-9/homelab-infra` per half (ADR-017, `scripts/bump-homelab-image.sh`, `scripts/open-homelab-bump-pr.sh`, secret `HOMELAB_INFRA_TOKEN` in environment `homelab-infra`).
- Never push to `main`; PR + green `ci-ok`, squash-merge. Never commit secrets (gitleaks hook + CI).
- Conventions: warnings are errors; Conventional Commits with the scope list in `.githooks/commit-msg`.
- Plan: `~/.claude/plans/pasted-content-id-bbc6-the-project-stateful-charm.md`.
