# Tasks: Transaction Disputes

Format: `[ID] [P?] [Story] description`. Tasks within a PR run in order unless marked [P] (parallel).

## PR1 — Walking skeleton (critical path)
- [x] T001 Repo, settings, hooks, gitleaks, .editorconfig, locked restore
- [x] T002 Compose: vault, bootstrap, postgres, redis, keycloak (realm import), migrate, api, web
- [x] T003 Vault AppRole bootstrap; Vault config provider in the API
- [x] T004 BFF: login/logout/user, encrypted Redis tickets, refresh-on-validate, JSON 401/403
- [x] T005 nginx app + auth origins, security headers per location
- [x] T006 React shell: session gate, error state with traceId
- [x] T007 CI: secrets, backend, frontend, images + Trivy, E2E, ci-ok
- [x] T008 Playwright: login, logout-requires-password, CSRF, headers

## PR2 — Spec and design
- [x] T010 Constitution, spec, plan, tasks, ADR-001..010
- [ ] T011 System-design document (tech-neutral) and neutrality gate

## PR3 — Transactions (US1)
- [ ] T020 `customer_id` denormalisation, indexes, Owner filter + architecture test
- [ ] T021 Seeder (relative dates), roles/grants migration
- [ ] T022 GET /transactions (keyset, protected cursor) + GET /transactions/{id}
- [ ] T023 EXPLAIN test: index scan, no sort
- [ ] T024 [P] Transactions page with zod `validateSearch`

## PR4 — Disputes (US2, US3, US4)
- [ ] T030 Dispute aggregate, transition table, eligibility chain (unit tests incl. 119/120/121 days)
- [ ] T031 Repositories, UoW, idempotent POST /disputes, GET list/detail with ETag
- [ ] T032 Withdraw with If-Match; DB-stamped audit
- [ ] T033 Concurrency tests (23505 raw SQL, barrier interleaving)
- [ ] T034 Luhn/SA-ID detector (server) + `validation-cases.json` parity
- [ ] T035 [P] Dispute form (react-hook-form + zod), history, detail

## PR5 — Agent (US5)
- [ ] T040 Agent queue/detail/transitions, segregation of duties, view logging
- [ ] T041 [P] Agent UI

## PR6 — Ops hardening
- [ ] T050 OTel + security events, rate limits, CSP/header/cookie tests, GHCR publish

## PR7 — Final docs
- [ ] T060 README criteria table, runbook, threat model (STRIDE + POPIA), test catalogue, AI-use disclosure
