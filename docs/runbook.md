# Runbook

Operational notes for the dispute portal. Everything here is grounded in the repository; anything that is a target
or an assumption is labelled as one. Nothing in this file has been measured against a production deployment, because
there is none ([ADR-009](decisions/009-local-and-ci-deploy.md)): the stack runs locally under Docker Compose and in CI.

## 1. Service map

| Component | Compose service | Network | Notes |
|---|---|---|---|
| nginx (SPA, `/api`, `/bff`, Keycloak origin) | `web` | edge, app | Only published container. Ports `127.0.0.1:${APP_PORT:-8080}` and `127.0.0.1:${AUTH_PORT:-8180}`. JSON access log on stdout with `request_id` |
| API (.NET 10) | `api` | app, data | Stateless; health on an unpublished management port 8081 (`/health/live`, `/health/ready`; `src/DisputePortal.Api/Shared/Health/ManagementSettings.cs`). Readiness checks Postgres and Redis |
| Migrate job | `migrate` | data | Same image with `--migrate`; uses the `dispute-migrate` AppRole (schema owner) |
| Postgres 17 | `postgres` | data | Volume `pgdata`. Holds the app database `disputes`, the `keycloak` database and the DataProtection keys |
| Redis 8 | `redis` | data | Volume `redisdata`, AOF, `noeviction`, ACL user `dispute_api` limited to `~bff:*`. Holds encrypted session tickets and refresh locks only |
| Keycloak 26.4 | `keycloak` | app, data | Realm import from `deploy/keycloak/` |
| Vault 1.20 | `vault`, `bootstrap` | data | Server mode, file storage in volume `vault-data`, one key share (local only) |
| Grafana LGTM | `lgtm` | edge, app | Only with `--profile observability` |

## 2. Service level objectives

These are assumed targets from `specs/001-transaction-disputes/spec.md` (NFR-5). They have not been measured: the
repository has no load test (k6 is a stretch item in the plan) and no production traffic.

| SLO | Target | SLI (what to measure) |
|---|---|---|
| Availability | 99.9% (about 43 minutes of budget in a 30-day month, arithmetic only) | Share of non-5xx responses on `/api` and `/bff`; plus `/health/ready` success from the management port |
| Read latency | p95 at most 300 ms | `GET /api/v1/*` server duration |
| Write latency | p95 at most 500 ms | `POST /api/v1/disputes`, `POST /api/v1/disputes/{id}/withdraw`, `POST /api/v1/agent/disputes/{id}/transitions` server duration |

Server-side limits that bound the tail: request timeout 30 s (`Program.cs`), Postgres `statement_timeout` 5 s and EF
command timeout 10 s (`Shared/Persistence/DatabaseSettings.cs`, `PersistenceRegistration.cs`), pool of 20.

## 3. Telemetry: names exactly as in code

Custom meters (source: `src/DisputePortal.Api/Shared/Observability/`):

| Instrument | Type | Tag | Values | Meter |
|---|---|---|---|---|
| `security.events` | counter | `type` | `authz_denied`, `bola_404`, `csrf_rejected`, `rate_limited`, `session_refresh_failed` | `DisputePortal.Security` |
| `audit.events` | counter | `action` | `agent_dispute_viewed`, `agent_dispute_transitioned` | `DisputePortal.Security` |
| `disputes.submitted` | counter | `reason` | reason code | `DisputePortal.Disputes` |
| `disputes.transitions` | counter | `to` | status name | `DisputePortal.Disputes` |
| `disputes.eligibility_rejections` | counter | `rule` | rule code | `DisputePortal.Disputes` |
| `disputes.resolution.duration` | histogram, seconds | `to` | `Accepted`, `Rejected` (buckets 60 s to 14 days) | `DisputePortal.Disputes` |

Framework meters registered in `Telemetry.cs`: `Microsoft.AspNetCore.Hosting`, `Microsoft.AspNetCore.Server.Kestrel`,
`System.Net.Http`, `Npgsql`. Request latency comes from the ASP.NET Core instrument `http.server.request.duration`
(OpenTelemetry semantic-convention name; not defined in this repository's code) and the backend's own translation of
dots to underscores (for example Prometheus `http_server_request_duration_seconds_bucket`). [UNVERIFIED: the exported
names in the LGTM stack were not inspected for this document; check them in Grafana Explore before writing the rules.]

Traces: ASP.NET Core, HttpClient and Npgsql, plus a `DisputePortal.Api` activity source. `url.query` is removed from
server spans; client spans keep the URL without its query. Health endpoints are not traced. Telemetry is exported only
when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (`Telemetry.cs`).

Logs: JSON console. Structured events of note: `agent.dispute.viewed` and `agent.dispute.transitioned` (ids and agent
subject only), `Session refresh was refused; the session was ended`, `Request refused with 400 ...`.

## 4. Alerts

Proposed alert rules. They are design targets for a deployment that has an alert manager; none is deployed here.
Thresholds marked "starting point" are guesses to be tuned against a baseline that does not exist yet.

| Alert | Condition | Severity | First action |
|---|---|---|---|
| 5xx rate | 5xx share of requests above 1% for 5 minutes (starting point; the 99.9% SLO allows 0.1%) | page | Section 8, "API errors" |
| Read p95 | p95 of `GET` requests above 300 ms for 10 minutes | ticket | Check Postgres load and the `statement_timeout` log lines; see the keyset query plan test `TransactionPlanTests` for the expected plan |
| Write p95 | p95 of `POST` requests above 500 ms for 10 minutes | ticket | Same; also check Redis latency (the session store is read on every request) |
| Readiness | `/health/ready` failing for 2 minutes | page | Section 8, "Dependency down" |
| `security.events{type="bola_404"}` | **rate**, not a count: increase over 5 minutes above a baseline learned in production. Starting point: more than 20 per customer-facing minute across all replicas | ticket | The counter counts every 404 answered to a customer (the owner filter cannot tell someone else's id from an unknown id, documented in `SecurityMetrics.cs`). Normal noise is stale bookmarks; a probe for foreign ids is a sustained rate from one session. Find the session in the nginx log by `request_id`, and the subject in the API log by trace id |
| `security.events{type="authz_denied"}` | Rate above baseline for 5 minutes | ticket | Role misconfiguration after a realm change, or a customer probing `/agent` |
| `security.events{type="csrf_rejected"}` | Any sustained rate | ticket | A client without the `X-CSRF: 1` header: a broken deploy of the SPA, or a forged cross-site request |
| `security.events{type="rate_limited"}` | Rate above baseline for 5 minutes | ticket | A runaway client or abuse. Limits are per replica (Section 9) |
| `security.events{type="session_refresh_failed"}` | Rate above baseline for 5 minutes, or any burst after a Keycloak change | page if it follows a Keycloak or secret rotation | Section 7 (client secret) and Section 8 (Keycloak down). A refused refresh signs the user out (`Shared/Security/SessionValidator.cs`) |
| `disputes.resolution.duration` | p95 above the business target | ticket | Business signal; the target is not defined in the spec [UNVERIFIED] |

## 5. Observability profile (local)

```
OTEL_ENDPOINT=http://lgtm:4317 docker compose --profile observability up -d --wait
```

- `OTEL_ENDPOINT` feeds the API's `OTEL_EXPORTER_OTLP_ENDPOINT` (`docker-compose.yml`). If the `api` container was
  already running, `up -d` recreates it with the new variable.
- Grafana is published on `127.0.0.1:3000`. The image is `grafana/otel-lgtm` (digest-pinned in the compose file).
- Contradiction with the plan: the plan says the profile generates a Grafana admin password. The compose file does
  not set one, so the image's default credentials apply. [UNVERIFIED: the default for this image was not checked.]
  The port is bound to loopback only.
- Not verified for this document: the profile was not started (another agent held the Docker stack), so no dashboard,
  trace or metric name above has been seen in Grafana.

## 6. Backup and restore

**Status: design targets for a production deployment. Nothing below was run or measured, and the local stack has no
backup job.** The targets come from the system design (`docs/design/transaction-dispute-portal-system-design.md`
sections 5.5 and 8.4): RPO 15 minutes and RTO 1 hour, both labelled "assumed" there.

| What | In backup scope | Why |
|---|---|---|
| Postgres `disputes` database (disputes, history, transactions, customers, DataProtection keys) | Yes. Base backup plus continuous WAL archiving for point-in-time recovery, encrypted, held in a separate failure domain | System of record. The DataProtection keys live here, so a restore also restores the ability to read cursors and session tickets |
| Postgres `keycloak` database | Yes, same mechanism | Users, credentials and the realm state after import |
| Vault storage and the unseal key and root token | Yes, by the platform's secrets-manager procedure | Without them the AppRole credentials cannot be re-issued |
| Redis | No | Session tickets only; loss means everyone signs in again |
| Compose `secrets` volume | Regenerable locally by `bootstrap`; in production replace with platform identity | See the "secret zero" caveat in the README |

Local equivalent, for a demo database only (the volume names are prefixed by the compose project name `dispute-portal`):

```
docker compose exec postgres pg_dump -U postgres -d disputes -Fc > disputes.dump     # backup
docker compose exec -T postgres pg_restore -U postgres -d disputes --clean --if-exists < disputes.dump   # restore
```

[UNVERIFIED: these two commands were not run. `pg_restore --clean` into a database whose roles and grants already
exist is the intended path; a restore into a fresh volume must first run `deploy/postgres/roles.sql` and the migrations.]

Restore drill (design): restore the latest base backup and WAL to a scratch instance, run `--migrate` against it, start
the API against it, and record the elapsed time against the 1 hour RTO. The design doc notes the 1 hour RTO erodes as
the transaction table grows, and names warm-standby promotion and partitioning as the follow-up.

## 7. Secret rotation

Credentials are read once at start (`Shared/Secrets/VaultConfigurationProvider.cs`), so every rotation below ends in
a rolling restart of `api` (and a re-run of `migrate` where noted).

**Vault AppRole (`dispute-api`, `dispute-migrate`)**
- `secret_id` TTL is 24 hours and the roles are CIDR-bound to the data subnet (`deploy/bootstrap/bootstrap.sh`).
  Every `docker compose up` issues fresh `secret_id`s, writes them to the `secrets` volume, and tidies expired ones.
- Rotate on demand: re-run `docker compose up -d bootstrap`, then restart `api` and `migrate`. Old `secret_id`s stay
  valid until their TTL expires; to revoke one immediately use `vault write auth/approle/role/<role>/secret-id-accessor/destroy`
  (run on the Vault host through the cluster's own exec path).
- A new `role_id` requires deleting and recreating the role, which `bootstrap` does not do.
- Production: replace AppRole-by-file with platform identity (README "Known limits").

**Database passwords** (`dispute_app`, `dispute_owner`, `keycloak` role)
- Generated once into the `secrets` volume and set on first Postgres start (`deploy/postgres/init.sh`). The bootstrap
  does not rotate them (`gen` only creates missing files).
- Rotate: `ALTER ROLE ... PASSWORD` in Postgres, write the same value to Vault `kv/dispute-portal/database` (or
  `database-owner`), restart `api`. The Keycloak role password is also in `secrets/keycloak/db_password`. The path is
  possible but not scripted; none of it was exercised. [UNVERIFIED]

**Keycloak client secret (`dispute-portal-bff`)**
- Source of truth: the `secrets` volume file `keycloak/client_secret`, copied to Vault `kv/dispute-portal/keycloak`
  and injected into Keycloak at start through `KC_CLIENT_SECRET` (realm import uses `${ENV}` placeholders).
- Rotate: change the client secret in Keycloak and the Vault value together, then restart `api`. A mismatch shows up
  as `session_refresh_failed` and failed sign-ins (`BffOidcEvents` logs `OIDC sign-in failed`). Realm import runs
  `--import-realm`, which does not overwrite an existing realm, so a changed placeholder on an existing volume does
  not reach Keycloak [UNVERIFIED: Keycloak's default import strategy is skip-if-exists]; rotate through the admin API
  or reset the Keycloak database.

**DataProtection keys**
- Stored in Postgres (`PersistKeysToDbContext`, application name fixed in
  `Shared/Persistence/PersistenceRegistration.cs`). `--migrate` creates the first key.
- They encrypt session tickets in Redis and protect pagination cursors. The framework rolls keys by its own schedule;
  an expired key is replaced (`DataProtectionKeyTests.ARevokedExpiredOrNotYetActiveKeyIsReplaced`).
- Emergency revocation: revoking keys invalidates every ticket and cursor, so all users sign in again. Do it by
  revoking through the DataProtection API on a running replica or by deleting the key rows as `dispute_owner`, then
  restarting `api`; `--migrate` re-creates a key if none is usable. [UNVERIFIED: not exercised.]
- Keys are stored unencrypted at rest, guarded by grants (README "Known limits"); the upgrade is
  `ProtectKeysWithCertificate` with a key held in Vault.

**Redis password**: a generated file in the `secrets` volume, also written to Vault `kv/dispute-portal/redis` and to
`users.acl`. Rotate by deleting `redis/password`, re-running `bootstrap`, and restarting `redis` and `api` together.
[UNVERIFIED: not exercised.]

## 8. Incident steps

First, for any incident: take the `traceId` from the error (the SPA shows it as "Reference"), find the request in the
nginx JSON log by `request_id` (`docker compose logs web`) and the API log line with the same trace.

**API errors (5xx)**
1. `docker compose ps` and `docker compose logs --tail=200 api`.
2. The management endpoint is unpublished; probe from inside: `docker compose exec api dotnet DisputePortal.Api.dll --healthcheck` (what the compose healthcheck runs).
3. Dependency errors map to 503 with a code: `idp_unavailable` (Keycloak), a session-store 503 (Redis), database unavailable. Fix the named dependency; the API fails closed and keeps running.

**Dependency down**
- Redis: login and every authenticated call return 503 (fail closed, `WhenRedisIsDownLoginAndSessionValidationFailClosedWith503`). Restart `redis`; users keep their cookies but tickets are lost only if the volume was.
- Keycloak: sign-in is 503; existing sessions keep working until the 60-second pre-expiry refresh, and an unreachable IdP during refresh keeps the session (`AnUnreachableIdpDuringRefreshIs503AndKeepsTheSession`).
- Postgres: readiness fails; EF retries 3 times; restore from backup if data is lost (Section 6).
- Vault sealed after a restart: `docker compose up -d bootstrap` unseals it (single local key share). `api` reads Vault only at start, so a sealed Vault does not hurt running replicas but blocks new ones.

**Suspected credential or session compromise**
1. Revoke sessions: rotate DataProtection keys (Section 7) so every ticket becomes unreadable, or flush the `bff:*` keys in Redis.
2. Disable the user in Keycloak; their access token lasts at most 5 minutes (realm `accessTokenLifespan` 300) and refresh then fails.
3. Rotate the secrets that may have leaked (Section 7).
4. Review `agent.dispute.viewed` and `agent.dispute.transitioned` log lines and `audit.events` for the affected agent subject.
5. POPIA: if personal information was accessed, see the "Breach notification" row of the POPIA mapping in [threat-model.md](threat-model.md) (no procedure is defined yet); that document is an engineering mapping, not legal advice.

**Rollback of a bad deploy**: images are published to GHCR by commit sha (`ghcr.io/maseko-lucky-9/dispute-portal-{api,web}:<sha>`); redeploy the previous sha. Migrations are forward-only EF migrations; the design doc says changes should be forward-compatible, and no down-migration is tested. [UNVERIFIED for production.]

## 9. Local-stack caveats

- **Rate limit**: 10 dispute submits a minute per subject per API replica (`RateLimits.cs`, setting
  `RateLimiting:DisputeSubmitsPerMinute`); 300 requests a minute overall. With `--scale api=3` the effective quota is
  about three times that. A demo script that submits quickly will see `429` with `Retry-After`.
- **The disputable pool shrinks.** A transaction can have only one active dispute, accepted or rejected disputes
  block it for good, and a customer may have at most 20 active disputes and 3 per transaction. The seeded data
  (`Shared/Persistence/DemoTransactions.cs`) is finite, so repeated demos and the E2E suite consume it. E2E tests pick
  a fresh disputable row each run (plan, finding 12); a long-lived local stack will eventually have none left.
- **Reset**: `docker compose down -v` removes every volume: Postgres (all disputes, the Keycloak realm state), Redis,
  Vault storage and the generated secrets. **It destroys all local demo data.** The next `up` regenerates secrets and
  re-seeds. The secrets volume and the Postgres volume must be reset together, because the database role passwords come
  from the secrets volume on first start (README).
- **Safari** and `Secure` cookies on `http://localhost` are unverified; use Chromium or Firefox.
- **Ports and subnets**: `APP_PORT`, `AUTH_PORT`, `APP_SUBNET`, `DATA_SUBNET` (see `.env.example`).
- Another stack using the same compose project name (`dispute-portal`) will share volumes with this one.

## 10. Data retention (design only)

The code has no retention job and no erasure endpoint. The design (`docs/design/...` section 5.5) says disputes and
history are kept for a regulator-driven period that is jurisdiction-dependent and an open assumption, transactions
follow the core-banking policy, the application role can never delete history, and erasure requests are met by
pseudonymising the customer display name while keeping structural records, subject to legal review. Keycloak keeps
login and admin events for 30 days (`eventsExpiration` 2592000 in the realm file). See the POPIA section of
[threat-model.md](threat-model.md).
