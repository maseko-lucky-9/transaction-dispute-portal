# Transactions Dispute Portal

Customers view their bank transactions and dispute them, with a historic view of every dispute.

> Backend `backend/src/`, frontend `frontend/`, E2E `e2e/`. Deeper material is in [docs/](docs/): the
> [runbook](docs/runbook.md), the [threat model and POPIA mapping](docs/threat-model.md), the
> [AI-use disclosure](docs/ai-use.md), the [time log](docs/time-log.md), the [ADRs](docs/decisions/) and the
> [system design](docs/design/transaction-dispute-portal-system-design.md).

## Run it

Needs Docker with Compose v2.27+ and nothing else on the host.

```
docker compose up --build
```

Then open <http://localhost:8080>. No `.env`, no make, no scripts: a one-shot `bootstrap` container generates
the secrets, initialises Vault and writes the AppRole files; `migrate` applies the EF Core migrations;
then the API and nginx start.

| URL | What |
|---|---|
| <http://localhost:8080> | The SPA (nginx), API under `/api`, BFF under `/bff` |
| <http://localhost:8180> | Keycloak login pages only (a separate origin; everything else returns 404) |

Port clash? `APP_PORT=9090 AUTH_PORT=9190 docker compose up --build`. Every redirect URI and issuer derives from
those two variables, but the realm is imported only into an empty database: on a stack that already has data, run
`docker compose down -v` first (this deletes the local demo data) or login fails with `invalid_request: redirect_uri`
(verified). Use Chromium or Firefox for the local demo (Safari's handling of `Secure` cookies on
`http://localhost` is unverified).

**API docs** (Swagger UI, off by default in every environment, [ADR-013](docs/decisions/013-interactive-api-docs.md)):
`OPENAPI_ENABLED=true docker compose up --build`, then <http://localhost:8080/api/swagger>. The document is at
`/api/openapi/v1.json`. Reading the contract works. "Try it out" works for `/api/*` too: sign in at `/bff/login` in the same browser, click
Authorize in the UI and enter `1` for `csrf` (the session cookie is sent by the browser on its own). Use `docs/openapi.json` and `api.http` for calls.

Demo users (local-only, in `deploy/keycloak/demo-users.json`):

| User | Password | Role |
|---|---|---|
| `thandi` | `Thandi-Demo-2026!` | customer |
| `sipho` | `Sipho-Demo-2026!` | customer |
| `agent` | `Agent-Demo-2026!` | dispute-agent |
| `agent2` | `Agent2-Demo-2026!` | dispute-agent |
| `agent3` | `Agent3-Demo-2026!` | dispute-agent |

The `keycloak-users` service adds them on every `docker compose up`; existing users are left alone, so a stack that
already has data gains the missing ones without a reset.

The two internal networks use `10.231.28.0/24` (data) and `10.231.29.0/24` (app); set `DATA_SUBNET` / `APP_SUBNET` if
they clash with a VPN or another Docker network. Compose never gives `web` (the internet-facing container) a route to
Postgres, Redis or Vault.

Reset everything (secrets, Vault, database): `docker compose down -v`. The secrets and the Postgres volume must be
reset together, because the database roles take their passwords from the secrets volume on first start.

## Build and test

```
dotnet build DisputePortal.slnx -warnaserror
dotnet test  backend/tests/DisputePortal.UnitTests
dotnet test  backend/tests/DisputePortal.IntegrationTests     # Testcontainers: needs Docker
dotnet build DisputePortal.slnx -c Release && scripts/coverage.sh   # both suites merged, fails below the line-coverage floor
dotnet tool restore && (cd backend/tests/DisputePortal.UnitTests && dotnet tool run dotnet-stryker)   # mutation score of Domain, report only
npm --prefix frontend ci --ignore-scripts && npm --prefix frontend run lint && npm --prefix frontend run typecheck \
  && npm --prefix frontend run test:coverage && npm --prefix frontend run build
docker compose up -d --wait --build && (cd e2e && npm ci && npx playwright install chromium && npx playwright test)   # E2E against the real stack
```

## CI and deploy

`.github/workflows/ci.yml`, required check `ci-ok`. Jobs and the image publish are path-scoped per half
([ADR-012](docs/decisions/012-backend-frontend-layout-and-path-scoped-cicd.md)): on `main`, `publish` pushes
`ghcr.io/maseko-lucky-9/dispute-portal-{api,web}:<sha>` only for the half whose image changed. Then `bump-homelab`
opens a pull request in the private `maseko-lucky-9/homelab-infra` that changes that image's one `digest:` line in
`apps/dispute-portal/kustomization.yaml`; merging it (by hand) is the deploy, ArgoCD syncs and the migrate hook re-runs
([ADR-017](docs/decisions/017-homelab-image-bump-prs.md), merge and rollback in the [runbook](docs/runbook.md)). At most
one bump PR per half is open; a newer one closes the older. A manual run (`workflow_dispatch`) is a dry run by default;
from `main` with `dry_run` false it re-publishes the chosen halves (`half`: both, api or web) and opens their bump PRs.

Secrets: `HOMELAB_INFRA_TOKEN`, a fine-grained token for `homelab-infra` only (Contents and Pull requests: read and
write), stored in the `homelab-infra` environment (deployments from `main` only, no repository-level copy) and used only
by `bump-homelab`; without it that job fails with an error and no PR is opened. Before opening a PR the job verifies the
image's build provenance (signed by `ci.yml` on `main` for that commit). Everything else uses the run's own
`GITHUB_TOKEN`. While homelab-infra's `master` is unprotected, that token can deploy to the cluster by pushing to
`master` (ADR-017).

## Measured results

Raw summary lines (macOS), all from `main` at `74b1d94`, 2026-11-16 (`dotnet build -c Release -warnaserror`, then
`scripts/coverage.sh`; `npm run lint`, `typecheck`, `test:coverage` and `build` in `frontend/`):

```
Build       dotnet build DisputePortal.slnx -c Release -warnaserror: 0 Warning(s), 0 Error(s)
Unit        Passed!  - Failed:     0, Passed:   708, Skipped:     0, Total:   708, Duration: 16 s - DisputePortal.UnitTests.dll (net10.0)
Integration Passed!  - Failed:     0, Passed:   501, Skipped:     0, Total:   501, Duration: 54 s - DisputePortal.IntegrationTests.dll (net10.0)
Backend     line coverage, unit + integration merged: 92.41% (gate: scripts/coverage.sh fails below 80%)
Web         Test Files  50 passed (50)
                 Tests  1231 passed (1231)
            Lines : 99.61% ( 1032/1036 )   Branches : 96.25% ( 926/962 )   (vitest v8 coverage; floor 80% in frontend/vite.config.ts)
E2E         24 passed (42.4s)   (clean clone of 74b1d94, see "Verified on a clean clone")
```

From CI (the E2E count was also re-run locally, above) and earlier local runs:

| Figure | Value | Where it is gated or reported |
|---|---|---|
| E2E (Playwright, full compose stack, real Keycloak) | 24 tests (auth 7, back-channel logout 2, transactions 5, disputes 5, agent 2, work assignment 3); job `e2e` green on `main` at `13a50f7` (run 43890281574) | CI job `e2e` |
| Stryker.NET mutation score, `DisputePortal.Domain` | 87.26% (185 killed, 26 survived, 1 not covered; CI run 43890281574 on `main` at `13a50f7`) | CI job `stryker`, report only (`backend/tests/DisputePortal.UnitTests/stryker-config.json` has `break: 0`) |
| Manual mutation checks (window rule, Owner filter, If-Match, plus CSRF, Luhn, 23505, partial unique index, concurrency; back-channel logout) | 14 mutations all caught, 1 probe survived (likely an equivalent mutant); back-channel logout: 55 mutations (53 code, nginx rule, Keycloak URL) all caught, after one equivalent mutant was removed from the code and two tests were strengthened; dispute assignment (ADR-019): the review fixes and the assignee invariant, results in their own sections | [docs/mutation-checks.md](docs/mutation-checks.md) with raw output |
| CodeQL (C#, JavaScript/TypeScript, Actions) | first analysis: 0, 1, 0 results; the one finding (a regex in the CSP build guard) is fixed | GitHub code scanning (default setup) |

Latency (k6 smoke test, `perf/k6/smoke.js`, two 3-minute runs on a laptop with three API replicas, 12 req/s reads plus
submit-and-withdraw writes, 0 failures, 0 x 429; the thresholds were shown to gate with a negative control that exits 99):
read p95 8.66 ms and 8.27 ms, write p95 17.78 ms and 20.93 ms, against the SLO targets of 300 ms and 500 ms. Raw output and
caveats are in [docs/perf.md](docs/perf.md). It is not a capacity test (per-subject rate limits cap the rate; the API sat
at about 10% CPU) and it does not measure availability, so the 99.9% target remains an assumption.

## Verified on a clean clone

A fresh clone of `main` at `74b1d94` was brought up with
`APP_PORT=9090 AUTH_PORT=9190 DATA_SUBNET=10.231.38.0/24 APP_SUBNET=10.231.39.0/24 docker compose -p dp-verify up --build -d --wait`
(no `.env`, no make), isolated from another stack running on the default ports and subnets. Raw observations from that
run (2026-11-16):

| Check | Result |
|---|---|
| Startup | api, web, postgres, redis, keycloak healthy and vault running in 59 s with cached layers; `bootstrap`, `migrate` and `keycloak-users` exit 0 |
| No cookie | `GET /api/v1/disputes` with `X-CSRF: 1` is `401 application/problem+json`, no redirect |
| No `X-CSRF` | `403 csrf_required` problem document with a `traceId`. CSRF is checked before authentication, so a bare `curl` with no headers gets 403, not 401. The one exception is a GET or HEAD of the flag-gated API docs (see "API docs") |
| `--scale api=3` | after one browser sign-in, 24 requests on that session cookie all 200, spread 8 / 8 / 8 across the three replicas (nginx JSON access log, `upstream` field) |
| `APP_PORT=9090` | the whole run used 9090 / 9190 on fresh volumes: sign-in, E2E and the browser pass below all worked |
| Headers | `nosniff`, `Referrer-Policy: no-referrer`, `Permissions-Policy`, COOP and a strict CSP on `/`, `/api` and `/bff`; `no-store` on `/api` and `/bff`; auth origin `/admin` and `/realms/master` return 404 |
| E2E | `24 passed (42.4s)` with `COMPOSE_PROJECT_NAME=dp-verify` (the admin-delete back-channel test runs `kcadm` in that project's Keycloak; without it, it reaches the other stack's Keycloak and fails) |
| Browser pass | customer (home, transactions, disputes, a dispute, the dispute form) and agent (queue, a dispute) in light and dark: 14 pages, 0 axe violations (WCAG 2.1 A/AA tags), 0 CSP violations; the only console error per context is the expected `401` from `/bff/user` before sign-in. Screenshots in [docs/screenshots/](docs/screenshots/) |
| Observability | not re-run on `74b1d94`. On `15c3b67` (2026-11-04) with `OTEL_ENDPOINT` set: `disputes_submitted_total{reason=...} = 1`, `security_events_total{type="csrf_rejected"} = 1`, `{type="authz_denied"} = 1`; one Tempo trace for the submit with redis and 9 Npgsql spans |
| GHCR | publishing is path-scoped, so `74b1d94` (Keycloak theme only) published nothing. `dispute-portal-api:latest` resolves to the digest of the `13a50f7` tag and `dispute-portal-web:latest` to the `1f1f1a6` tag (`docker buildx imagetools inspect`, anonymous pull) |

## Criteria → evidence

The brief grades 11 criteria. The plan names seven of them (Primary: requirements fit, architecture and scalability,
security. Important: domain modelling, API contracts, testing, observability); the other four are not enumerated in the
plan or the spec, so they are not mapped here. [UNVERIFIED which four.] Paths are relative to the repository root;
test names are xUnit methods (`backend/tests/DisputePortal.*Tests/<File>.cs`), Vitest files (`frontend/src/...`) or Playwright titles
(`e2e/tests/*.spec.ts`).

### The brief

| Requirement | Implemented in | Proven by | Gap |
|---|---|---|---|
| Customers view their transactions | `backend/src/DisputePortal.Api/Features/Transactions/`, `frontend/src/components/transactions-page.tsx` | `TransactionsTests.WalkingEveryPageReturnsAllOfMyTransactionsInKeysetOrderWithNoDuplicatesOrGaps`; e2e `customer pages through their own transactions without duplicates, with a11y and CSP clean` | |
| Customers dispute a transaction | `Features/Disputes/`, `backend/src/DisputePortal.Domain/Disputes/Dispute.cs`, `frontend/src/components/dispute-form-page.tsx` | `DisputesTests.SubmittingCreatesADisputeWithLocationETagAndOneTimelineEntry`; e2e `a customer disputes a transaction, sees it in history, replays it safely, withdraws it and disputes again` | |
| Historic view of disputed transactions | `GET /api/v1/disputes` in `Features/Disputes/DisputesEndpoints.cs`; `frontend/src/components/disputes-page.tsx`, `dispute-timeline.tsx` | `DisputesTests.TheHistoryWalksEveryStatusNewestFirstAndFiltersByStatus`; `TheDisputeShowsInMyHistoryAndBlocksTheTransaction`; `frontend/src/components/disputes-page.test.tsx` | |
| Runnable Dockerfile | `Dockerfile` (targets `web` and `api`, default `api`; chiseled non-root runtime, digest-pinned bases) | CI job `images` builds both targets and runs Trivy; CI job `e2e` runs the built images through `docker compose up` twice (second `up` proves bootstrap is idempotent); the `up --build` path was run on a clean clone of `74b1d94` (see "Verified on a clean clone") | CI starts the stack with `--no-build` from the built images |
| README with build, run and test | this file, "Run it" and "Build and test" | Same commands are what `.github/workflows/ci.yml` runs | A clean-clone run by a second person is [UNVERIFIED] |

### Graded criteria named in the plan

| Criterion | Where | Evidence | Gap |
|---|---|---|---|
| Requirements fit | US1-US5 and FR/NFR table below | Every row below names a test | NFR-5 (SLOs) and the retention part of NFR-7 have no test (see below) |
| Architecture and scalability | [ADR-001](docs/decisions/001-vertical-slices-and-domain-project.md), [ADR-002](docs/decisions/002-ef-core-repositories-unit-of-work.md), [ADR-006](docs/decisions/006-keyset-pagination-and-signed-cursors.md); stateless API, nginx upstream `resolve` in `frontend/nginx/default.conf.template`, keyset indexes | `TransactionPlanTests.TheKeysetQueryIsAnIndexScanWithNoSortOn50kRows`; `ArchitectureTests.TheDomainReferencesNeitherEfCoreNorAspNetCore`; `ProductionAuthStackTests.ParallelRefreshesAcrossTwoReplicasRedeemOnceAndKeepTheSession`; `BackchannelLogoutTests.AReplayToAnotherReplicaIsRefusedToo`; `scripts/check-constraints.sh` | `docker compose up --scale api=3` was not exercised by any test in the repository (back-channel logout across three replicas was checked by hand, ADR-011: sessions signed in on one replica were ended by Keycloak's call to another, and the call failed over when that replica stopped); the capacity numbers in the design doc are assumptions (the doc says so) |
| Security | [Threat model](docs/threat-model.md), ADR-004, 005, 007, 008 | Rows in the security table below | Residual risks listed in "Known limits" and the threat model |
| Domain modelling | `backend/src/DisputePortal.Domain/` (aggregate, transition table, eligibility chain, `Money`), [ADR-003](docs/decisions/003-dispute-state-machine.md) | `DisputeLifecycleTests.EveryMoveIsAllowedExactlyWhenItIsInTheTable`, `TerminalStatusesHaveNoWayOut`; `DisputeEligibilityPolicyTests.TheWindowIncludesDay120AndEndsOnDay121`; `DisputeEligibilityPolicyTests`; Stryker 87.26% on this project | Stryker figure is from CI on `13a50f7`, the last commit that changed backend code |
| API contracts | `docs/openapi.json` (code-first, committed), [ADR-007](docs/decisions/007-http-status-policy-and-idempotency.md), RFC 9457 errors with `code` and `traceId` | `OpenApiTests.TheDocumentMatchesTheCommittedSnapshot`, `TheAgentRoutesDocumentIfMatchTheETagAnd412And428`, `TakeNextDocuments200WithAnETagAnd204AndTheQueueItsAssignmentFilter`; `frontend/src/api/contract.test.ts`; CI step "TS types match the committed OpenAPI snapshot" | `api.http` (41 requests: 37 checked against a live stack; the agent customer-transactions one and the three assignment ones (the `assignment` filter, take next, release) are new and were not run against one; not tested in VS Code or JetBrains) lets a reviewer call every endpoint |
| Testing | [Test catalogue](#test-catalogue) | Counts above; CI; manual mutation checks with raw output in [docs/mutation-checks.md](docs/mutation-checks.md) | Manual checks cover the mutations listed there (including the ADR-019 assignment review fixes and invariant), not every code path |
| Observability | `backend/src/DisputePortal.Api/Shared/Observability/`, `frontend/nginx/default.conf.template` (JSON access log with `$request_id`), [runbook](docs/runbook.md) | `DisputeMetricsTests`, `Bola404MetricTests`, `ResolutionDurationMetricTests`, `AgentDisputeDetailTests.EverySuccessfulViewIsAuditedWithIdsOnlyAndAMissingOneIsNot`, `AgentTransitionTests.TheNoteTextIsNeverLoggedOnlyIdsAndCodes` | No automated test asserts the OTLP export or the Grafana view. It was checked by hand on a clean clone (see "Verified on a clean clone"); that check is not repeatable in CI |

### User stories and requirements (`specs/001-transaction-disputes/spec.md`)

| Item | Implemented in | Proven by |
|---|---|---|
| US1.1 keyset paging, no duplicates or gaps (FR-1) | `Features/Transactions/TransactionRepository.cs`, `Shared/Paging/KeysetCursor.cs` | `TransactionsTests.WalkingEveryPage...`, `RowsWithIdenticalDatesArePagedWithoutDuplicatesOrGaps`, `TheDefaultPageIs25AndTheLastPageHasNoCursor`; `TransactionPlanTests` |
| US1.2 foreign id is 404, never 403 | Owner query filter, `Shared/Persistence/DisputePortalDbContext.cs`; `Shared/Security/CustomerScopeFilter.cs` | `TransactionsTests.AnotherCustomersTransactionIs404LikeAMissingOne`; `OwnerFilterTests.*`; `ArchitectureTests.EveryEntityWithACustomerIdHasTheOwnerFilter`; e2e `a customer can never read another customer's transaction (BOLA), and an agent is refused` |
| US1.3 pending, credit, over-120-days shown as not disputable with a reason | `Domain/Eligibility/*Rule.cs`, `Features/Transactions/TransactionItem.cs` | `TransactionEligibilityTests.FlagsAndReasonsFollowTheRulesIncludingTheDay120Boundary`; `DisputeEligibilityPolicyTests.APostedDebitInsideTheWindowIsDisputable`, `OnlyAPostedTransactionIsDisputable`, `ACreditIsNotDisputable` |
| US2.1 201 with Location and ETag (FR-2) | `Features/Disputes/DisputeCommands.cs` | `DisputesTests.SubmittingCreatesADisputeWithLocationETagAndOneTimelineEntry` |
| US2.2 422 lists every violated rule | `Domain/Eligibility/DisputeEligibilityPolicy.cs` | `DisputesTests.EveryBrokenRuleIsReportedAtOnceAs422`; `DisputeEligibilityPolicyTests.EveryViolationIsReportedTogetherInRuleOrder` |
| US2.3 409 `dispute_exists` with the existing id | `Features/Disputes/DisputeConflictProblem.cs` | `DisputesTests.AnExistingDisputeIs409DisputeExistsWithItsId` |
| US2.4 retry with the same id returns the original | client-generated id is the key (ADR-007) | `DisputesTests.ReplayingTheSameIdReturnsTheOriginalWithoutASecondRow`; `DisputeConcurrencyTests.TheSameSubmissionRacingItselfGivesOne201AndOne200WithTheSameBody`, `AnEfRetryAfterALostCommitAcknowledgementReplaysInsteadOfInsertingTwice` |
| US2.5 card or SA ID number rejected, 400 `sensitive_data` | `Domain/Disputes/SensitiveNumberDetector.cs`, `frontend/src/lib/validation.ts` | `SensitiveNumberDetectorTests` and `frontend/src/lib/validation.test.ts` (both read `frontend/src/lib/validation-cases.json`); `CreateDisputeRequestValidatorTests.ACardNumberInTheDescriptionIsSensitiveData` |
| US2.6 inline errors, no request sent | `frontend/src/lib/dispute-form.ts`, `components/dispute-form.tsx` | `frontend/src/components/dispute-form-page.test.tsx`, `frontend/src/lib/dispute-form.test.ts`; e2e `an invalid filter shows inline errors and sends no request` (transactions filters; the dispute-form case is covered by the Vitest files) |
| US3 history with timeline showing actor type, never the agent's identity (FR-3) | `Features/Disputes/TimelineEntry.cs` | `DisputeSecurityTests.TheTimelineNeverCarriesActorIds`; e2e `an agent reviews and accepts a customer dispute; the customer sees the outcome but not who decided` |
| US4 withdraw: 412 stale, 428 missing `If-Match`, 409 terminal (FR-4) | `Features/Disputes/DisputeCommands.cs`, `DisputeETag.cs` | `DisputeSecurityTests.WithdrawingWithoutIfMatchIs428`, `AStaleOrLooseIfMatchIs412WithTheCurrentETag`, `AfterAWithdrawalTheOldETagIs412AndTheNewOne409InvalidTransition`; `DisputeConcurrencyTests.TwoWithdrawalsWithTheSameETagGiveOne200AndOne412WithTheNewETag` |
| US5 agent queue and transitions; agent cannot act as customer; customer cannot reach `/agent`; dual-role refused; views logged; one agent per dispute: claim, take next, release, only the reviewer decides ([ADR-019](docs/decisions/019-dispute-assignment-claim-take-next-release.md)) | `Features/Agent/`, `Shared/Security/SingleRoleRequirement.cs`, `Domain/Disputes/Dispute.cs` (`AssigneeId`, `NotAssigneeException`) | `AgentTransitionTests.AValidMoveIs200WithTheNewETagAndTheAgentAsActor`, `ACustomerIs403AndADualRoleSessionIs403RoleConflict`; `AgentQueueTests`; `AgentAssignmentTests`; `AgentTakeNextTests.TwoAgentsTakingNextAtOnceGetDifferentDisputes`, `EightParallelTakeNextsNeverHandOutADisputeTwice`; `DisputeLifecycleTests.AnotherAgentCannotDecideOrReleaseAndNothingChanges`; `DisputeSecurityTests.TheCustomerViewNeverCarriesTheAssignee`; `AgentCustomerTransactionsTests`, `AgentCustomerTransactionsAuditTests`; `DisputeSecurityTests.AnAgentIs403OnEveryCustomerDisputeRoute`; `AgentDisputeDetailTests.EverySuccessfulViewIsAuditedWithIdsOnlyAndAMissingOneIsNot`; e2e `the agent role is separate from the customer role`; e2e `two agents taking next at the same moment get different disputes`, `only the reviewing agent may decide or release, and the customer never sees who`, `the workbench: take next lands under "In review by you", a colleague can only read it, and after release can claim it` (`frontend/src/components/agent-workbench.tsx`) |
| FR-5 sign in and out through Keycloak, roles `customer` and `dispute-agent` | `Features/Bff/`, `deploy/keycloak/dispute-portal-realm.json` | e2e `customer signs in through Keycloak, sees the shell, no CSP violations, no axe violations`, `after logout the password is asked for again (Keycloak session really ended)`; `ProductionAuthStackTests.LogoutDeletesTheTicketAndRedirectsToKeycloakEndSessionWithIdTokenHint` |
| FR-6 reason catalogue | `Features/Disputes/DisputeReasonRepository.cs` | `DisputesTests.TheReasonCatalogueListsSevenReasonsInDisplayOrder` |
| NFR-2 stateless, scalable | ADR-001, ADR-006 | see "Architecture and scalability" above |
| NFR-3 reliability: idempotent submit, optimistic concurrency, readiness, bounded startup retry | `Shared/Health/`, `Shared/Secrets/VaultConfigurationProvider.cs`, `Shared/Persistence/PersistenceRegistration.cs` | `DisputeConcurrencyTests.*`; `ManagementPortTests.HealthAnswersOnTheManagementPort`; `VaultConfigurationProviderTests.AnUnavailableVaultFailsAfterTheBoundedRetry`, `TheRetryReallyBacksOff`; `ProductionAuthStackTests.WhenRedisIsDownLoginAndSessionValidationFailClosedWith503` |
| NFR-4 observability | see "Observability" above | see above |
| NFR-5 SLOs 99.9% / p95 300 ms reads, 500 ms writes | [runbook](docs/runbook.md) (assumed targets), `perf/k6/smoke.js`, [docs/perf.md](docs/perf.md) | k6 smoke run (manual, not in CI): read p95 about 8 ms, write p95 18 to 21 ms at 12 req/s. **Does not prove** capacity, tail behaviour under saturation, or the 99.9% availability target |
| NFR-6 one-command start, CI runs full-stack E2E | `docker-compose.yml`, CI job `e2e` | CI job `e2e` (24 Playwright tests) |
| NFR-7 POPIA: masked account numbers, no PII in logs, agent views logged, retention | `Features/Transactions/Account.cs` (`MaskedNumber`), [threat model](docs/threat-model.md) | `AgentTransitionTests.TheNoteTextIsNeverLoggedOnlyIdsAndCodes`; `AgentDisputeDetailTests.EverySuccessfulViewIsAuditedWithIdsOnlyAndAMissingOneIsNot`. **Retention is a documented design, not implemented code** |

### Security controls

| Control | Implemented in | Proven by |
|---|---|---|
| BFF: no token in JS, HttpOnly + Secure + SameSite=Lax cookie | `Features/Bff/BffAuthentication.cs` | `ProductionAuthStackTests.SignInIssuesAnHttpOnlySecureLaxSessionCookie`, `TheSessionTicketInRedisIsEncryptedAndExpiresWithTheAbsoluteLifetime`; `AuthHardeningTests.TheTicketSlidesFor15MinutesLivesAtMost8HoursAndHoldsOnlyThreeTokens` |
| OIDC code flow with PKCE S256 | `BffOidcEvents.cs`, realm client `dispute-portal-bff` | `ProductionAuthStackTests.TheAuthorizeRequestUsesTheCodeFlowWithPkceS256AndOnlyOpenidProfile` |
| JSON 401/403 on `/api`, never a redirect | cookie events, `Shared/Security/ProblemAuthorizationResultHandler.cs` | `ProductionAuthStackTests.AnApiCallWithoutASessionIsA401ProblemNotARedirect`; e2e `anonymous visitor sees Sign in; /bff/user is a 401 ProblemDetails, not a redirect` |
| CSRF header `X-CSRF: 1` | `Shared/Security/CsrfMiddleware.cs` | `CsrfRuleTests.TheHeaderIsRequiredOnApiAndOnNonGetBff`; `AuthHardeningTests.AnyCsrfHeaderValueOtherThanExactlyOneIsRefused`; e2e `CSRF header is required on /api` |
| Token refresh; refresh failure ends the session (the fallback revocation bound, 5 minutes) | `Shared/Security/SessionValidator.cs`, `RefreshSingleFlight.cs` | `ProductionAuthStackTests.ANearlyExpiredAccessTokenIsRefreshedAndTheRolesAreReRead`, `ARefusedRefreshEndsTheSession`; `RefreshSingleFlightTests` |
| Back-channel logout: a session logged out at Keycloak (a logout at Keycloak itself, an admin deleting the session; both verified by E2E) is dead on the next request; natural expiry at Keycloak is left to the refresh bound ([ADR-011](docs/decisions/011-backchannel-logout.md)) | `Features/Bff/BackchannelLogoutEndpoint.cs`, `LogoutTokenValidator.cs`, sid index in `Shared/Security/RedisTicketStore.cs`; management port only (`Shared/Health/ManagementPortMiddleware.cs`); realm client `backchannel.logout.*` | `LogoutTokenValidatorTests` (one negative test per spec rule); `BackchannelLogoutTests.*`; `ProductionAuthStackTests.ABackchannelLogoutFromKeycloakEndsTheSessionOnTheNextRequest`; `SessionStoreTests.RemovingBySessionIdDeletesEveryTicketOfThatSessionAndItsIndexButNoOtherSession`; e2e `a logout at Keycloak itself, not through the app, ends the app session on the next request`, `an admin ending the session in Keycloak ends the app session on the next request`; edge 404 in e2e `edge: ...` |
| Logout ends the Keycloak session | `BffEndpoints.cs` | e2e `after logout the password is asked for again (Keycloak session really ended)`; `LogoutWithAnotherSessionsSidIs400AndKeepsTheSession` |
| Fail-closed ownership (BOLA) | Owner named query filter; `IgnoreQueryFilters` only under `Features/Agent` | `OwnerFilterTests.WithNoCurrentCustomerTheAppSeesNoTransactionsOrAccounts`; `ArchitectureTests.OnlyTheAgentFeatureMayBypassQueryFilters`; BOLA tests in the US1.2 and US3 rows |
| Segregation of duties | `SingleRoleRequirement.cs` | `ProductionAuthStackTests.ASessionHoldingBothRolesIs403RoleConflict`; `*ADualRoleSessionIs403RoleConflict*` on each route family |
| Least-privilege DB role, append-only audit with DB-stamped time | `deploy/postgres/roles.sql`, grants in EF migrations | `DatabasePrivilegeTests.TheAppRoleCannotWriteOrAlterWhatItShouldNot`, `TheAppRoleCanSubmitAndMoveADisputeAndTheDatabaseStampsTheHistory` |
| DB backstop for one active dispute per transaction | partial unique index `ux_disputes_one_active_per_transaction` | `ReadModelConstraintTests.ASecondActiveDisputeOnOneTransactionIsRefusedByThePartialUniqueIndex`; `DisputeConcurrencyTests.TwoSubmitsForOneTransactionHeldTogetherGiveOne201AndOne409DisputeExists` |
| DB backstop: an assignee exactly while a dispute is under review ([ADR-019](docs/decisions/019-dispute-assignment-claim-take-next-release.md), the contract step) | CHECK `ck_disputes_assignee`, migration `AgentAssignmentInvariant` | `ReadModelConstraintTests.TheAssigneeCheckRefusesAnUnderReviewDisputeWithoutOneAndAnyOtherWithOne`, `TheAssigneeCheckAcceptsEveryLegalPairing`; `AgentAssignmentInvariantMigrationTests` |
| Signed, user-and-filter-bound cursors | `Shared/Paging/KeysetCursor.cs` | `TransactionsTests.ATamperedCursorIs400InvalidCursor`, `AnotherCustomersCursorIs400InvalidCursor`, `ACursorReusedWithOtherFiltersIs400InvalidCursor` |
| No raw interpolated SQL | convention + architecture rule | `ArchitectureTests.NoRawSqlCallIsBuiltFromAnInterpolatedOrConcatenatedString` |
| Rate limits per subject (10 dispute submits a minute, 300 a minute overall, per replica) | `Shared/Security/RateLimits.cs` | `DisputeSecurityTests.TheEleventhSubmitInAMinuteIs429WithRetryAfter` (proves the 10-a-minute submit bucket only; the 300-a-minute overall bucket has no test) |
| Forwarded-header and host trust | `Shared/Hosting/HostingRegistration.cs` (forwarded headers on the public port only; `api-mgmt` admitted for the management port only) | `AuthHardeningTests.UnderTheDefaultConfigurationAForeignHostIs400`, `TheRateLimitPartitionUsesTheForwardedClientOnlyBehindTheTrustedProxy`, `RedirectUrisComeFromConfigurationNotFromTheHostHeader`; `BackchannelLogoutTests.TheInternalHostIsRefusedOnThePublicPort`, `OnlyPeersOnTheDataNetworkReachTheEndpointAndForwardedForIsIgnoredThere` |
| CSP, edge headers, `no-store`, Keycloak admin not exposed | `frontend/nginx/*.conf` ([ADR-008](docs/decisions/008-csp-edge-headers-and-keycloak-origin.md)) | e2e `edge: security headers per location, no-store on the API, Keycloak admin is not exposed`; `ProductionAuthStackTests.ApiAndBffResponsesAreNoStore`; zero CSP violations asserted in the e2e sign-in and transactions tests |
| Credentials only from Vault: AppRole per service in compose, or Kubernetes auth with the pod's service-account token on a cluster, https only; Vault's self-issued server certificate trusted by an exact pin (`Vault__PinnedServerCertificateFile`: SHA-256 match, host name and dates still checked) where no CA chain exists ([ADR-016](docs/decisions/016-vault-kubernetes-auth.md)) | `Shared/Secrets/`, `deploy/bootstrap/bootstrap.sh`, `deploy/vault/policies/` | `VaultConfigurationProviderTests.MigrateReadsOnlyTheOwnerPath`, `TheApiPathsLandUnderTheirConfigurationPrefixes`, `KubernetesLoginPostsTheRoleAndTheTrimmedTokenToTheDefaultMount`, `TheTokenIsReReadOnEveryAttempt`, `KubernetesOverPlainHttpFailsAtOnceWithoutARequest`, `TheStartupDetailNamesTheCauseButNeverTheToken`; `VaultServerCertificatePinTests.*` (pin, name mismatch, dates, pin file); `VaultPinnedServerCertificateTests.*` (real TLS: correct pin loads, wrong pin and no pin are rejected). Vault policies and CIDR binding themselves are not covered by an automated test (they run in the CI e2e job but nothing asserts them) |
| Secrets not in git | `.githooks/pre-commit`, `.gitleaks.toml` | CI job `secrets` (gitleaks) |
| Dependencies and images | `.github/dependabot.yml`, `scripts/check-image-pins.sh` | CI: `dotnet restore --locked-mode`, vulnerable-package check, `npm audit --audit-level=high`, Trivy on both images |

## Test catalogue

| Layer | Where | Count (`main` at `74b1d94`, 2026-11-16) | Proves | Does not prove |
|---|---|---|---|---|
| Unit (xUnit 2.9.3) | `backend/tests/DisputePortal.UnitTests/` | 708 | The state-machine table, every eligibility rule and the 119/120/121-day boundary with a fake clock, `Money`, the card and ID detector (shared cases with the web), request validators, cursor protectors, CSRF rule and its management-port exemption, the management-port split (port, internal host, peer network), logout-token validation (one negative test per Back-Channel Logout rule), refresh single-flight, Vault provider retry and the Vault TLS pin (against a real HTTPS listener on 127.0.0.1), seeding, ETag format | Anything that needs real SQL, grants, HTTP, Redis or Keycloak. Services run against substitutes, so repository SQL is not exercised here |
| Integration (WebApplicationFactory + Testcontainers Postgres and Redis, connecting as `dispute_app`) | `backend/tests/DisputePortal.IntegrationTests/` | 501 | Endpoint behaviour and status codes, owner filter and BOLA 404s, idempotent submit, 23505 backstop and forced interleavings, `If-Match` 412/428, DB grants and append-only history, production auth stack (401 not 302, CSRF, cookie flags, ticket encryption, refresh, Redis down), back-channel logout (ticket and sid-index deletion, generic refusals, replay across two hosts, reachability by port, host and peer, per-peer limit, no ids in logs), keyset plan (index scan, no Sort, 50k rows), OpenAPI snapshot, architecture rules, security metrics | A real Keycloak (the IdP is a test double here; e2e covers the real one), real nginx headers, TLS, browser behaviour, multi-replica scale-out beyond the two-replica refresh test, performance |
| Frontend (Vitest + Testing Library + MSW) | `frontend/src/**/*.test.ts(x)` | 1231 in 50 files (`npm run test:coverage`: `Tests  1231 passed (1231)`) | Form and filter validation, shared `validation-cases.json` parity, server-error mapping (400, 409, 412, 422), focus and `traceId` display, role guards, session gate, axe checks in `a11y.test.tsx`, generated-type contract | The real API (MSW stubs it), real CSP enforcement, real browsers beyond jsdom |
| E2E (Playwright, Chromium, full compose stack) | `e2e/tests/*.spec.ts` | 24 | Real login and logout through Keycloak, back-channel logout from real Keycloak (a logout at Keycloak itself; an admin deleting the session with `kcadm`), journeys for customer and agent, work assignment (two agents racing take next, reviewer-only decide and release, the workbench), BOLA over HTTP, CSRF, per-location headers, nginx's own 404 for the back-channel path, zero CSP violations, axe, Keycloak admin 404 | Other browsers (Safari unverified), load, failure injection, anything that needs a second replica |
| Coverage gate | `scripts/coverage.sh`, CI job `backend` | 92.41% line | Total line coverage of `DisputePortal.Api` and `DisputePortal.Domain` (migrations excluded) stays at or above 80% | That the covered lines are asserted on; branch coverage is not gated. The frontend has its own 80% threshold in `frontend/vite.config.ts` |
| Stryker.NET (non-blocking) | CI job `stryker`, `backend/tests/DisputePortal.UnitTests/stryker-config.json` | 87.26% on Domain (CI, `13a50f7`) | How many injected Domain mutants the unit tests kill. Not in `ci-ok`'s `needs`, so it can never block a merge | Mutation strength of the API, authorization or SQL code (only `DisputePortal.Domain` is mutated) |
| Trivy | CI job `images` | n/a | No fixable CRITICAL finding in the `api` and `web` images at build time (`--ignore-unfixed`, `.trivyignore` empty) | HIGH findings, unfixed findings, runtime exposure, the base-image supply chain beyond digest pinning |
| gitleaks | pre-commit hook (if installed) and CI job `secrets` | n/a | No secret matching the default ruleset in the checked history; the documented demo passwords are the only allowlist entry (`.gitleaks.toml`) | Secrets that do not match a rule; secrets outside git |
| Other CI gates | `.github/workflows/ci.yml`, `scripts/check-constraints.sh` | n/a | path scoping (`scripts/test-changed-paths.sh`), the homelab bump scripts (`scripts/test-bump-homelab-image.sh`, `scripts/test-open-homelab-bump-pr.sh`), `-warnaserror` build, `dotnet format whitespace`, locked restore, vulnerable packages, `npm audit --audit-level=high`, lint, typecheck, TS types equal the OpenAPI snapshot, banned packages and unpinned images; the Keycloak login theme drift check against homelab-infra (`.github/workflows/keycloak-theme-sync.yml`, [ADR-018](docs/decisions/018-keycloak-theme-drift-check.md)); CodeQL default setup (C#, JavaScript/TypeScript, Actions) on every push to `main` | Whether CodeQL findings block a merge: it is not part of `ci-ok` |

## Known limits (deliberate, with the upgrade path)

| Limit | Why it is acceptable here | Upgrade path |
|---|---|---|
| Rate limits are per API replica | N replicas allow about N times the quota | Redis-backed limiter |
| Keycloak does not retry a failed back-channel logout call, and does not reliably send one when a session merely expires | A call lost to an API or Redis outage (503; the jti claim is released) or a 429 from a flood, and every natural expiry, leave those sessions to the 5-minute refresh bound | An outbox-style sweep that asks Keycloak which sids are still alive |
| Vault runs with one key share, and `bootstrap` plays the trusted orchestrator that delivers each service's AppRole files ("secret zero") | Local development only; the AppRoles are bound to the `data` network, which `web` is not on | Auto-unseal, no root token; the API already supports Kubernetes auth instead of files ([ADR-016](docs/decisions/016-vault-kubernetes-auth.md)), compose keeps AppRole |
| Issued AppRole secret_ids stay valid for 24 h (tidy removes only expired ones) | Bounded by the network binding and the TTL | Single-use secret_ids with response wrapping |
| DataProtection keys sit unencrypted in Postgres, guarded by grants | A Redis write alone cannot forge a session | `ProtectKeysWithCertificate` with a key held in Vault |
| Keycloak and the data stores are not `read_only` | They need writable state; capabilities are dropped | Per-image tmpfs mounts |
| Database credentials are read once at start-up | Rotation means a rolling restart | Vault dynamic database credentials |
| A customer's submits are serialised by one advisory lock ([ADR-014](docs/decisions/014-advisory-lock-on-dispute-caps.md)); an idle transaction that loses its connection can hold it until the server notices | Only that customer's submits answer 503, the lock lasts one short transaction, and submits are limited to 10 a minute per user | `idle_in_transaction_session_timeout` on the application connection |
| A caller can learn that a client-chosen dispute id exists (409 versus 201), though never its content | The id is random, 122 bits, seen only by its owner, and submits are limited to 10 a minute | Scope the idempotency key per customer |
| The app's database role can change a dispute's own columns without a matching history row (history itself is append-only) | It needs a stolen app credential; grants are column-scoped | A terminal-state guard trigger and DB-written history |
| The card and ID number check deters accidental disclosure, not deliberate obfuscation by the customer about their own number (five or more separators, words between digit groups, invisible non-format code points such as variation selectors, circled or superscript digits, fullwidth punctuation as separators) | Free text is also never logged; PCI scope is documented in the threat model | None without a tokenisation service |
| "Latest dispute" ordering uses the replica's clock | Replicas share NTP; the effect is one wrong button for a re-dispute within the skew | A database-stamped `created_at` |

## Cut-line decisions

The plan's cut line, in order, is: dark/light screenshots, then the Stryker job, then a minimal-table agent UI; the
plan schedules the decision for 2026-11-08.

- Stryker: kept, as a non-blocking CI job and a reported score.
- Agent UI: built (`frontend/src/components/agent-queue-page.tsx`, `agent-dispute-page.tsx`, `agent-workbench.tsx`,
  `agent-customer-transactions.tsx`), covered by Vitest and five E2E tests (`agent.spec.ts`, `work-assignment.spec.ts`).
- Dark/light screenshots: kept. Eight are committed in [docs/screenshots/](docs/screenshots/) (customer transactions,
  disputes and dispute form, agent dispute; light and dark), taken on a clean clone of `74b1d94`. The theme follows
  `prefers-color-scheme` and the toggle (`frontend/src/components/theme-toggle.tsx`) pins a choice.
- None of the three was cut.

## Next

Out of scope for this submission (listed as out of scope in `specs/001-transaction-disputes/spec.md` or as deferred or
stretch in the plan):

- Attachments and notifications.
- A live cloud deployment. Today "deploy" means images published to GHCR with provenance on `main`, per half: a backend change publishes only the api image and a frontend change only the web image ([ADR-009](docs/decisions/009-local-and-ci-deploy.md), [ADR-012](docs/decisions/012-backend-frontend-layout-and-path-scoped-cicd.md)), plus a homelab-infra pull request per published image that deploys it to the homelab cluster when merged ([ADR-017](docs/decisions/017-homelab-image-bump-prs.md)).
- An outbox and event publishing.
- Agent TOTP (stretch).
- Run the k6 smoke test in CI against the compose stack (today it is a documented manual run), and add a capacity test that raises the per-subject limits to find where latency degrades.
- Dynamic database credentials and a distributed rate limiter (see "Known limits").
