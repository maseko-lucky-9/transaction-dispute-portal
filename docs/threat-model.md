# Threat model

STRIDE per trust boundary, with the mitigation that exists in code or tests and what remains. Paths are relative to the
repository root; test names are in `tests/DisputePortal.IntegrationTests/` or `tests/DisputePortal.UnitTests/` unless a
path says otherwise. A claim without a named test has no automated proof, and says so.

## 1. Assets and boundaries

Assets: customer transactions and disputes (financial, personal), dispute free text, agent decisions and notes, the
audit history, session tickets, AppRole and database credentials, DataProtection keys.

```
Browser ──(1)── nginx ──(2)── API ──(3)── Keycloak / Vault / Redis / Postgres
                                  └─(4) customer role vs agent role, inside the API
```

Compose networks: `edge` (nginx only), `app` (nginx, API, Keycloak), `data` (stores, Vault, API, Keycloak). nginx has no
route to Postgres, Redis or Vault (`docker-compose.yml`). Everything runs over plain HTTP locally; TLS is a deployment
concern and is not tested.

## 2. STRIDE

### Boundary 1: browser to nginx

| | Threat | Mitigation (where, proof) | Residual |
|---|---|---|---|
| S | Session theft or forgery | Opaque HttpOnly, Secure, SameSite=Lax `dp_session` cookie; ticket contents encrypted in Redis (`ProductionAuthStackTests.SignInIssuesAnHttpOnlySecureLaxSessionCookie`, `TheSessionTicketInRedisIsEncryptedAndExpiresWithTheAbsoluteLifetime`) | Cookie theft from a compromised endpoint device. No device binding. Over http locally `Secure` cookies depend on the browser (Safari unverified) |
| S | Login phishing or password guessing | Keycloak on its own origin; brute-force detection on, password policy `length(12) and notUsername and notEmail` (`deploy/keycloak/dispute-portal-realm.json`); nginx `limit_req` on login POSTs (`web/nginx/default.conf`) | No MFA for customers or agents (TOTP is a stretch item, not built) |
| T | CSRF | `X-CSRF: 1` required on `/api` and non-GET `/bff`; SameSite=Lax; no CORS policy (`CsrfRuleTests`, `AuthHardeningTests.AnyCsrfHeaderValueOtherThanExactlyOneIsRefused`, e2e `CSRF header is required on /api`) | None known beyond browser bugs |
| T | XSS reading or changing the page | `script-src 'self'`, `default-src 'none'`, `frame-ancestors 'none'`, no tokens in JS (ADR-004, ADR-008); e2e fails on any CSP violation | `style-src 'unsafe-inline'` (Radix runtime styles). An XSS could still act as the user through the session cookie, because the browser sends it automatically |
| R | User denies an action | Append-only `DisputeStatusChange` with DB-stamped time and actor type/id (`DatabasePrivilegeTests.TheAppRoleCanSubmitAndMoveADisputeAndTheDatabaseStampsTheHistory`) | Not hash-chained; a DB owner could alter it (README "Known limits") |
| I | Sensitive data cached or leaked in headers | `Cache-Control: no-store` on `/api` and `/bff` (`ProductionAuthStackTests.ApiAndBffResponsesAreNoStore`); `Referrer-Policy: no-referrer`; headers repeated per location (e2e `edge: security headers per location...`) | **No HSTS header is configured anywhere in `web/nginx/` or `src/`** although ADR-008 mentions it; add it when TLS terminates in front |
| D | Flooding | nginx `limit_req` (20 r/s per IP on `/bff`, 5 r/s on login POSTs), `client_max_body_size 64k`; API per-subject limiter | Per-IP nginx limits are generous because Docker Desktop NATs clients to one address; no WAF or CDN |
| E | Reach Keycloak admin or master realm | Auth origin allow-lists paths, everything else 404 (e2e `edge: ... Keycloak admin is not exposed`) | Allow-list correctness depends on the regex in `default.conf` |

### Boundary 2: nginx to API

| | Threat | Mitigation | Residual |
|---|---|---|---|
| S | Spoofed client address or host (`X-Forwarded-For`, `Host`) | `KnownNetwork` is the app subnet, `ForwardLimit 1`, `AllowedHosts=localhost` (`Shared/Hosting/HostingRegistration.cs`; `AuthHardeningTests.UnderTheDefaultConfigurationAForeignHostIs400`, `TheRateLimitPartitionUsesTheForwardedClientOnlyBehindTheTrustedProxy`) | Anyone on the `app` network can impersonate nginx |
| T | Tampered request in transit | Internal network, plain HTTP | No mTLS or TLS inside the stack |
| R | Cannot trace a request | `$request_id` in nginx JSON log forwarded as `X-Request-ID`; `traceId` in every problem body | Request-id to API log correlation relies on the API logging it; not asserted by a test |
| I | Management/health exposure | Health only on port 8081, not published, not served on the public port (`ManagementPortTests.HealthIsNotReachableOnThePublicPort`, `TheManagementPortDoesNotServeTheApi`); OpenAPI not served in production (`OpenApiTests.AProductionHostDoesNotServeTheDocument`) | None known |
| D | Slow or oversized requests | 64k body cap, 30 s request timeout (`Program.cs`) | No per-connection limits beyond nginx defaults |
| E | n/a at this hop beyond the above | | |

### Boundary 3: API to Keycloak, Vault, Redis, Postgres

| | Threat | Mitigation | Residual |
|---|---|---|---|
| S | A rogue service obtains credentials | AppRole per service, CIDR-bound to the `data` network, `secret_id` TTL 24 h, policies split (`deploy/vault/policies/*.hcl`: API cannot read the owner credentials; `VaultConfigurationProviderTests.MigrateReadsOnlyTheOwnerPath`) | "Secret zero": `bootstrap` delivers the files, and anyone who can read the `secrets` volume owns the stack. One Vault key share, root token on the volume. Local only (ADR-005) |
| S | Forged ID token on refresh | Refreshed ID token validated; unknown signing key ends the session (`AuthHardeningTests.ARefreshedIdTokenSignedWithAnUnknownKeyEndsTheSessionAndRemovesTheTicket`) | |
| T | Redis write access forges a session | Tickets are DataProtection-encrypted and the keys are in Postgres, not Redis; Redis ACL user limited to `~bff:*` (`bootstrap.sh`) | Someone with Postgres read access to the keys plus Redis write could forge a ticket; keys are unencrypted at rest (README "Known limits") |
| T | SQL injection | EF Core parameterisation; architecture rule banning interpolated raw SQL (`ArchitectureTests.NoRawSqlCallIsBuiltFromAnInterpolatedOrConcatenatedString`) | |
| T | App credential rewrites history | `dispute_app` has INSERT only on history, with `occurred_at` excluded (`DatabasePrivilegeTests.TheAppRoleCannotWriteOrAlterWhatItShouldNot`) | The app role can still change a dispute's own columns without a history row (README "Known limits") |
| R | Agent denies viewing or deciding | `agent.dispute.viewed` and `agent.dispute.transitioned` logged with ids and subject; `audit.events` counter; transitions stored with actor (`AgentDisputeDetailTests.EverySuccessfulViewIsAuditedWithIdsOnlyAndAMissingOneIsNot`) | Log retention and tamper-resistance are the log platform's job, not provided here |
| I | Credentials, tokens or text in logs/traces | Ticket holds only id and refresh tokens; `url.query` dropped from server spans; note text never logged (`AgentTransitionTests.TheNoteTextIsNeverLoggedOnlyIdsAndCodes`) | Logs contain dispute, transaction and agent-subject identifiers (pseudonymous, see POPIA below) |
| I | Cursor tampering or reuse across users | DataProtection-protected cursors bound to user and filters (`TransactionsTests.ATamperedCursorIs400InvalidCursor`, `AnotherCustomersCursorIs400InvalidCursor`) | |
| D | Dependency outage or slow query | Fail closed with 503 codes; Postgres `statement_timeout` 5 s; bounded Vault retry; resilience handler on the Keycloak client (`WhenRedisIsDownLoginAndSessionValidationFailClosedWith503`, `AnUnreachableIdpDuringRefreshIs503AndKeepsTheSession`) | Single instances of every store locally; no HA |
| D | Refresh-token stampede across replicas | Redis lock, one replica refreshes (`ParallelRefreshesAcrossTwoReplicasRedeemOnceAndKeepTheSession`, `RefreshSingleFlightTests`) | |
| E | Postgres role abuse | `dispute_app` is DML-only on named columns; schema owner is a separate role used only by `migrate` | |

### Boundary 4: agent versus customer (authorization inside the API)

| | Threat | Mitigation | Residual |
|---|---|---|---|
| S | A customer calls agent routes, or an agent calls customer routes | Policies `Customer` and `DisputeAgent` (`Shared/Security/Policies.cs`); `DisputeSecurityTests.AnAgentIs403OnEveryCustomerDisputeRoute`, `AgentQueueTests.ACustomerIs403OnTheQueue` | Role comes from the Keycloak ID token; a wrong realm assignment grants access |
| E | Segregation of duties: one session with both roles | 403 `role_conflict` (`ProductionAuthStackTests.ASessionHoldingBothRolesIs403RoleConflict`) | |
| I | BOLA: reading or withdrawing another customer's data | Fail-closed Owner filter; 404 not 403 (`OwnerFilterTests.*`, `TransactionsTests.AnotherCustomersTransactionIs404LikeAMissingOne`, `DisputeSecurityTests.AnotherCustomersDisputeIs404OnReadAndWithdrawLikeAMissingOne`, `ArchitectureTests.OnlyTheAgentFeatureMayBypassQueryFilters`) | `security.events{type=bola_404}` cannot separate probes from stale ids. A caller supplying a dispute id can learn that the id exists (409 vs 201) but not its content (README "Known limits") |
| I | Customer sees who decided | Timeline exposes actor type only (`DisputeSecurityTests.TheTimelineNeverCarriesActorIds`) | |
| T | Concurrent or stale decisions | `If-Match` with `xmin` ETag, 412/428; one active dispute per transaction by unique index (`AgentTransitionTests.TwoAgentsWithTheSameETagGiveOne200AndOne412WithTheNewETag`, `ReadModelConstraintTests.ASecondActiveDisputeOnOneTransactionIsRefusedByThePartialUniqueIndex`) | The 20-active and 3-per-transaction caps can be overshot by concurrent submits (README) |
| T | Free-text injection (control characters, card numbers) | Shared validation rules: `invalid_characters`, `sensitive_data` (`SensitiveNumberDetectorTests`; `AgentTransitionTests.APaddedCardNumberIsStillSensitiveData`) | Deters accident, not deliberate obfuscation |
| R | Agent acts for a customer | Not possible by routes; transitions record the agent as actor (`AgentTransitionTests.AValidMoveIs200WithTheNewETagAndTheAgentAsActor`) | A malicious agent can still decide wrongly; there is no four-eyes control |
| D | Abuse of dispute submission | 10 submits/min/subject/replica; caps of 20 active and 3 per transaction (`DisputeSecurityTests.TheEleventhSubmitInAMinuteIs429WithRetryAfter`; `DisputesTests.The21stOpenDisputeIs422TooManyActiveDisputes`) | Limiter is per replica |

## 3. Supply chain and build

gitleaks (hook and CI), `restore --locked-mode`, vulnerable-package check, `npm ci --ignore-scripts`, `npm audit
--audit-level=high`, Trivy on fixable CRITICAL, digest-pinned base images (`scripts/check-image-pins.sh`), SHA-pinned
Actions with a `contents: read` default token, SBOM and provenance on `main` images, Dependabot. Not present: CodeQL
(default setup reports `not-configured`), image signing (cosign), a ZAP baseline scan.

## 4. POPIA mapping

This is an engineering mapping of the system to the Protection of Personal Information Act 4 of 2013 (POPIA)
conditions. It is not legal advice and has not been reviewed by counsel or an information officer. The roles
(responsible party, operator) and the lawful basis are decisions for the deploying organisation.

**Personal information in scope**

| Data | Where | Notes |
|---|---|---|
| Customer display name, Keycloak subject id | `customers` table (`Features/Customers/Customer.cs`) | Contact details, addresses and credentials stay in Keycloak and are not copied (system design 5.6) |
| Login identity, email, credentials, login and admin events | Keycloak database; events kept 30 days (`eventsExpiration` 2592000) | Demo users are fictitious and local-only |
| Masked account number, merchant, amount, date | `accounts`, `transactions` | Full account numbers never enter the system (`MaskedNumber`); this data is financial information about a person |
| Dispute reason, free-text description, amount, resolution note | `disputes` | Free text may contain anything the customer types |
| Actor type and id per status change | `dispute_status_changes` | Agent ids are Keycloak subjects |
| Client IP address and user agent | nginx access log (`remote_addr`, `ua`; `$uri` is logged without the query string) | Personal information in many readings; retention is the log platform's |
| Session tickets | Redis, encrypted, expire in 8 hours | |

**Conditions, as implemented**

| POPIA theme | In this system | Gap |
|---|---|---|
| Purpose specification and further processing | Data is processed to display and dispute transactions and to review disputes | No documented lawful basis or purpose notice screen |
| Minimality | Display name and subject only; no contact data; masked numbers; customers see actor type, not agent identity | The description field is free text |
| Information quality | Customer enters the dispute; agents record notes | |
| Security safeguards | See sections 2 and 3: owner filter, least-privilege roles, encrypted sessions, no tokens in JS, headers, secrets in Vault | No encryption at rest is configured by this repository (database volume, Redis AOF); depends on the host. No TLS inside the stack |
| Sensitive-number detector | `Domain/Disputes/SensitiveNumberDetector.cs` rejects Luhn-valid 13 to 19 digit runs (card and SA ID numbers) after Unicode normalisation, server and web, with shared cases in `web/src/lib/validation-cases.json` (400 `sensitive_data`, nothing stored) | Deters accidental disclosure only; documented bypasses in the README. PCI scope: the system stores no card number by design, but a determined user can still type one in an obfuscated form |
| Logs and metrics carry no PII | Free text and note content are not logged (`TheNoteTextIsNeverLoggedOnlyIdsAndCodes`); metric tags are bounded codes, never ids (`SecurityMetrics.cs`) | Log lines carry dispute id, transaction id and agent subject (pseudonymous identifiers that become personal information when joined to the database). nginx logs carry IP and user agent. No automated test scans logs for personal data beyond the two tests named |
| Retention | Design only: regulator-driven period, history never deleted by the app role, deletion by a separately privileged process (system design 5.5) | **No retention job, schedule or period exists in code.** The period is an open assumption |
| Data-subject access and correction | A signed-in customer can read all their disputes and timelines (`GET /api/v1/disputes`, detail) | No export endpoint, no correction workflow, no erasure endpoint. Erasure vs audit conflict is resolved only on paper: pseudonymise the display name, keep structural records, subject to legal review |
| Agent access to personal information | Agent views are logged (`agent.dispute.viewed`) and counted | Logs are not tamper-evident; no periodic access review |
| Breach notification | A runbook step (Section 8 of [runbook.md](runbook.md)) tells the operator where to look | No notification procedure, regulator contact or timeline is defined. [UNVERIFIED for any legal deadline] |
| Cross-border transfer | None in the local stack. A cloud deployment would need a review | Not assessed |

## 5. Residual risk summary

1. No MFA, no TLS inside the stack, no HSTS, no encryption at rest configured by the repository.
2. Secret zero and single-share Vault are local-only designs; production needs platform identity.
3. DataProtection keys unencrypted in Postgres; a database-read plus Redis-write compromise could forge a session.
4. Per-replica rate limits and soft caps.
5. Retention, erasure, subject access export and breach notification are documented intentions, not features.
6. The history table is append-only for the app role but not hash-chained, and the app role can edit a dispute row in place.
7. The sensitive-number detector is a guard against accidents.
8. No CodeQL scan is configured, and no penetration test or ZAP scan has been run.
