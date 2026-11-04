# AI use disclosure

This project was built with Claude Code (Anthropic's CLI agent), driven by the repository owner. Most of the code,
tests and documentation in this repository was drafted by AI agents. This file says what is known from the repository
and what is not.

## What was done with AI

- **Plan.** The owner supplied the brief by voice and a request to follow an earlier project (SCAD) but with EF Core.
  Claude Code drafted the plan (`~/.claude/plans/pasted-content-id-bbc6-the-project-stateful-charm.md`, "rev 2"; it lives
  outside the repository) and revised it after three independent review lanes: the main thread checked against docs,
  a `security-auditor` agent (Opus), and an architecture/delivery `Plan` agent (Opus). The plan records the merged
  findings (the row count is not recorded in the repository [UNVERIFIED]) and where each was applied.
- **Implementation.** The plan assigns backend slices to a `csharp-dotnet` sub-agent (Sonnet; Opus for the auth, Vault
  and bootstrap work), the React app to a `frontend-framework-expert` sub-agent, tests to a `test-engineer`, and the
  system-design sections to two `system-design-drafter` agents. Git, `gh` and any MCP tooling stayed on the main
  thread. The repository itself does not record which sub-agent wrote which file. [UNVERIFIED per file]
- **Review.** The plan requires a `code-reviewer` on every PR and says the author never approves its own work; the
  security-auditor was to review PR1 and PR4. Some test comments cite reviewer findings (for example
  `tests/DisputePortal.IntegrationTests/AuthHardeningTests.cs`: "Gaps the independent test review found by mutation
  (PR #1)"; `specs/001-transaction-disputes/tasks.md` T040a: "PR4 review N7"). The review reports themselves are not in
  the repository, so the review outcomes cannot be audited from here. [UNVERIFIED]
- **This document set.** The README evidence tables, runbook, threat model and this file were drafted by a Claude Code
  executor agent from the repository contents, then checked by script for broken links.

## What the human owner decided

Stated in the plan as the owner's instructions or decisions: use C#/.NET, EF Core, PostgreSQL, Keycloak, Redis, nginx
and Vault; keep the frontend separate on the owner's usual stack; use the system-design and spec-kit skills; pick a
design pattern suited to disputes; use DI and repositories heavily; scope (customer portal plus agent back office);
local plus CI only for deploy; public repository; squash-merge on green CI; the due date. The goal-setting clarity layer
is recorded in the plan as waived, with the AI drafting answers for the owner to confirm. [UNVERIFIED whether each
draft was confirmed]

## What was generated

Everything else: the architecture details, ADR text, the domain model, endpoints, the React UI, tests, CI, compose and
Vault bootstrap, and documentation. The git history has a single author identity (the owner's), so commit authorship
does not distinguish human from AI work.

## How output was verified

- Automated: unit, integration (real Postgres and Redis via Testcontainers, connecting as the least-privilege role),
  frontend (Vitest) and Playwright E2E against the real compose stack with Keycloak; counts and commands are in the
  [README](../README.md).
- Gates: warnings as errors, line-coverage floor (80%), `scripts/check-constraints.sh`, gitleaks, Trivy, npm audit,
  vulnerable-package check; `ci-ok` is the required check on `main` (branch protection read with `gh api` on
  2026-11-04 [UNVERIFIED from the repository]).
- Mutation: Stryker.NET on the Domain project (report only). The plan also asks for manual "break the code, watch the
  test go red" checks on the window rule, the owner filter and the `If-Match` check; the repository does not contain
  their output. [UNVERIFIED]
- Known blind spots: AI-written tests can assert what the AI-written code does. The mutation score and the
  database-level tests reduce that risk but do not remove it. No human line-by-line review of the whole codebase is
  recorded. The AI-written documents may contain mistakes; every unverifiable statement in them is tagged
  `[UNVERIFIED]`.
