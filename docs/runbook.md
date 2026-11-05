# Runbook

Operational notes for the dispute portal. Everything here is grounded in the repository; anything that is a target
or an assumption is labelled as one. Nothing in this file has been measured against a production deployment, because
there is none ([ADR-009](decisions/009-local-and-ci-deploy.md)): the stack runs locally under Docker Compose and in CI.

## 1. Service map

| Component | Compose service | Network | Notes |
|---|---|---|---|
| nginx (SPA, `/api`, `/bff`, Keycloak origin) | `web` | edge, app | Only published container. Ports `127.0.0.1:${APP_PORT:-8080}` and `127.0.0.1:${AUTH_PORT:-8180}`. JSON access log on stdout with `request_id` |
| API (.NET 10) | `api` | app, data (alias `api-mgmt`) | Stateless; an unpublished management port 8081 serves health (`/health/live`, `/health/ready`; `backend/src/DisputePortal.Api/Shared/Health/ManagementSettings.cs`) and Keycloak's back-channel logout (`POST /bff/backchannel-logout`, only as `api-mgmt` from the data network; [ADR-011](decisions/011-backchannel-logout.md)). Readiness checks Postgres and Redis |
| Migrate job | `migrate` | data | Same image with `--migrate`; uses the `dispute-migrate` AppRole (schema owner) |
| Postgres 17 | `postgres` | data | Volume `pgdata`. Holds the app database `disputes`, the `keycloak` database and the DataProtection keys |
| Redis 8 | `redis` | data | Volume `redisdata`, AOF, `noeviction`, ACL user `dispute_api` limited to `~bff:*`. Holds encrypted session tickets (`bff:ticket:*`), the Keycloak-session index (`bff:sid:<sha256 of sid>`, ticket ids only), refresh locks and used logout-token ids (`bff:logout-jti:*`, hashed, 3-minute TTL) only |
| Keycloak 26.4 | `keycloak` | app, data | Realm import from `deploy/keycloak/`. Calls the API's back-channel logout at `http://api-mgmt:8081/bff/backchannel-logout` (`BACKCHANNEL_LOGOUT_URL`) whenever a session ends there |
| Vault 1.20 | `vault`, `bootstrap` | data | Server mode, file storage in volume `vault-data`, one key share (local only) |
| Grafana LGTM | `lgtm` | edge, app | Only with `--profile observability` |

## 2. Service level objectives

These are assumed targets from `specs/001-transaction-disputes/spec.md` (NFR-5). A k6 smoke test (`perf/k6/smoke.js`,
results in [perf.md](perf.md)) measured p95 of about 8 ms for reads and 18 to 21 ms for writes on a laptop with three API
replicas, with no failures and no 429s. That shows the latency targets are not at risk at that load; it is not a capacity
test, it does not measure availability, and there is no production traffic.

| SLO | Target | SLI (what to measure) |
|---|---|---|
| Availability | 99.9% (about 43 minutes of budget in a 30-day month, arithmetic only) | Share of non-5xx responses on `/api` and `/bff`; plus `/health/ready` success from the management port |
| Read latency | p95 at most 300 ms | `GET /api/v1/*` server duration |
| Write latency | p95 at most 500 ms | `POST /api/v1/disputes`, `POST /api/v1/disputes/{id}/withdraw`, `POST /api/v1/agent/disputes/{id}/transitions` server duration |

Server-side limits that bound the tail: request timeout 30 s (`Program.cs`), Postgres `statement_timeout` 5 s and EF
command timeout 10 s (`Shared/Persistence/DatabaseSettings.cs`, `PersistenceRegistration.cs`), pool of 20.

## 3. Telemetry: names exactly as in code

Custom meters (source: `backend/src/DisputePortal.Api/Shared/Observability/`):

| Instrument | Type | Tag | Values | Meter |
|---|---|---|---|---|
| `security.events` | counter | `type` | `authz_denied`, `backchannel_logout`, `backchannel_logout_rejected`, `bola_404`, `csrf_rejected`, `rate_limited`, `session_refresh_failed` | `DisputePortal.Security` |
| `audit.events` | counter | `action` | `agent_dispute_viewed`, `agent_dispute_transitioned`, `agent_queue_viewed` | `DisputePortal.Security` |
| `disputes.submitted` | counter | `reason` | reason code | `DisputePortal.Disputes` |
| `disputes.transitions` | counter | `to` | status name | `DisputePortal.Disputes` |
| `disputes.eligibility_rejections` | counter | `rule` | rule code | `DisputePortal.Disputes` |
| `disputes.resolution.duration` | histogram, seconds | `to` | `Accepted`, `Rejected` (buckets 60 s to 14 days) | `DisputePortal.Disputes` |

Framework meters registered in `Telemetry.cs`: `Microsoft.AspNetCore.Hosting`, `Microsoft.AspNetCore.Server.Kestrel`,
`System.Net.Http`, `Npgsql`. Request latency comes from the ASP.NET Core instrument `http.server.request.duration`
(OpenTelemetry semantic-convention name; not defined in this repository's code) and the backend's own translation of
dots to underscores (for example Prometheus `http_server_request_duration_seconds_bucket`). Checked on a clean clone with `--profile observability`: the exported counters appear as `disputes_submitted_total` and
`security_events_total`. The HTTP server duration histogram name was not inspected [UNVERIFIED]; check it in Grafana
Explore before writing the latency rules.

Traces: ASP.NET Core, HttpClient and Npgsql, plus a `DisputePortal.Api` activity source. `url.query` is removed from
server spans; client spans keep the URL without its query. Health endpoints are not traced. Telemetry is exported only
when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (`Telemetry.cs`).

Logs: JSON console. Structured events of note: `agent.dispute.viewed`, `agent.dispute.transitioned` and `agent.queue.viewed` (ids, filter, counts and agent
subject only; no customer data), `Session refresh was refused; the session was ended`, `Request refused with 400 ...`,
`Back-channel logout ended {SessionCount} session(s)` (a count, never a sid, sub or jti) and `Back-channel logout token
refused: {Reason}`, where `Reason` is one of `Malformed`, `Algorithm`, `Type`, `Signature`, `Issuer`, `Audience`,
`IssuedAt`, `Expired`, `NotYetValid`, `Events`, `Nonce`, `TokenId`, `SessionId`, `Replayed` (category
`DisputePortal.Api.Features.Bff.BackchannelLogout`).

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
| `security.events{type="rate_limited"}` | Rate above baseline for 5 minutes | ticket | A runaway client or abuse. Limits are per replica (Section 9). A 429 on `/bff/backchannel-logout` (HTTP server metrics with `http.route` `/bff/backchannel-logout` and status 429) is a signal on its own: that limit is 6000 a minute per peer (`RateLimiting:BackchannelLogoutsPerMinute`), so hitting it means a flood from inside the data network, and the throttled Keycloak calls fall back to the refresh bound |
| `security.events{type="session_refresh_failed"}` | Rate above baseline for 5 minutes, or any burst after a Keycloak change | page if it follows a Keycloak or secret rotation | Section 7 (client secret) and Section 8 (Keycloak down). A refused refresh signs the user out (`Shared/Security/SessionValidator.cs`) |
| `security.events{type="backchannel_logout_rejected"}` | Any sustained rate | ticket | Read `Reason` in the API log. `Issuer`, `Audience` or `Signature` right after a Keycloak change is a configuration mismatch (`KC_HOSTNAME` versus `Keycloak__Authority`, the client id, rotated realm keys: the API refetches keys on an unknown `kid`); `Replayed` or `Malformed` without a Keycloak change is a probe from inside the data network. Either way the affected sessions fall back to the 5-minute refresh bound |
| `security.events{type="backchannel_logout"}` | No alert; a dashboard series | none | One per Keycloak logout the API acted on (a logout at Keycloak, an admin ending sessions). A sudden burst is an admin "sign out all". Sessions that merely expire at Keycloak do not show here: they end at their next refresh |
| `disputes.resolution.duration` | p95 above the business target | ticket | Business signal; the target is not defined in the spec [UNVERIFIED] |

## 5. Observability profile (local)

```
OTEL_ENDPOINT=http://lgtm:4317 docker compose --profile observability up -d --wait
```

- `OTEL_ENDPOINT` feeds the API's `OTEL_EXPORTER_OTLP_ENDPOINT` (`docker-compose.yml`). If the `api` container was
  already running, `up -d` recreates it with the new variable.
- Grafana is published on `127.0.0.1:3000` only. The image is `grafana/otel-lgtm` (digest-pinned in the compose file).
- Sign in as `admin` with a generated password. `bootstrap` creates it in the `secrets` volume at
  `grafana/admin_password` only when the file is missing (so a second `up` does not rotate it, and it is never printed),
  and `lgtm` mounts that directory read-only and has Grafana read it through `$__file{}`. Read it with a one-off
  container (the volume is `<project>_secrets`, so `dispute-portal_secrets` for the default project):

  ```
  docker run --rm -v dispute-portal_secrets:/s:ro alpine cat /s/grafana/admin_password
  ```

- Anonymous access is switched off (`GF_AUTH_ANONYMOUS_ENABLED=false`). The image's own default is anonymous with the
  Admin role, which would make the password irrelevant.
- Grafana applies the admin password only when it creates its database, and `lgtm` has no data volume. To apply a
  changed password, recreate the container: `docker compose --profile observability rm -sf lgtm`, then `up -d` again.
- `GF_SECURITY_ADMIN_PASSWORD__FILE` does not work with this image: the `__FILE` suffix is handled by the official
  `grafana/grafana` image's `run.sh`, and `otel-lgtm` starts `./bin/grafana server` directly. With it, `admin`/`admin`
  still logged in (observed 2026-11-04), hence the `$__file{}` form.
- Verified once on a clean clone (2026-11-04): after one dispute submit, Prometheus had `disputes_submitted_total` and
  `security_events_total{type="csrf_rejected"|"authz_denied"}`, and Tempo had a trace for `POST /api/v1/disputes/` with
  a redis `session.retrieve` span and 9 Npgsql spans. No dashboard was built or screenshotted.
- `lgtm` joins the `edge` and `app` networks, so while the profile is on it sits on the subnet the API trusts for
  forwarded headers. Use the profile for local diagnosis only.

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

WARNING: the dump holds plaintext financial and personal data, so write it outside the repository (as below; `*.dump`
is also git-ignored), and `pg_restore --clean` DROPS and recreates existing objects in `disputes`.

Local equivalent, for a demo database only (the volume names are prefixed by the compose project name `dispute-portal`):

```
docker compose exec -T postgres pg_dump -U postgres -d disputes -Fc > ../disputes.dump     # backup
docker compose exec -T postgres pg_restore -U postgres -d disputes --clean --if-exists < ../disputes.dump   # restore
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
- Keycloak: sign-in is 503; existing sessions keep working until the 60-second pre-expiry refresh, and an unreachable IdP during refresh keeps the session (`AnUnreachableIdpDuringRefreshIs503AndKeepsTheSession`). Sessions ended at Keycloak while the API or Redis is down are not retried by Keycloak: the back-channel call returns 503 and those sessions end at their next refresh instead. Keycloak logs nothing at its default level for a failed call (checked), so the only trace is the API side (`session_store_unavailable` 503s on `/bff/backchannel-logout`, or no `Back-channel logout ended` line).
- Postgres: readiness fails; EF retries 3 times; restore from backup if data is lost (Section 6).
- Vault sealed after a restart: `docker compose up -d bootstrap` unseals it (single local key share). `api` reads Vault only at start, so a sealed Vault does not hurt running replicas but blocks new ones.

**Suspected credential or session compromise**
1. Revoke sessions: rotate DataProtection keys (Section 7) so every ticket becomes unreadable, or flush the `bff:*` keys in Redis.
2. End the user's sessions in Keycloak (admin console: Users, the user, Sessions, sign out; or `kcadm.sh delete sessions/<id> -r dispute-portal` inside the `keycloak` container) and disable the user. Keycloak calls the back-channel logout, so the app sessions end on their next request (API log `Back-channel logout ended n session(s)`). If that call was lost, the access token still lasts at most 5 minutes (realm `accessTokenLifespan` 300) and refresh then fails.
3. Rotate the secrets that may have leaked (Section 7).
4. Review `agent.dispute.viewed`, `agent.dispute.transitioned` and `agent.queue.viewed` log lines and `audit.events` for the affected agent subject.
5. POPIA: if personal information was accessed, see the "Breach notification" row of the POPIA mapping in [threat-model.md](threat-model.md) (no procedure is defined yet); that document is an engineering mapping, not legal advice.

**Rollback of a bad deploy**: images are published to GHCR by commit sha (`ghcr.io/maseko-lucky-9/dispute-portal-{api,web}:<sha>`), but only for the image a commit changed (ADR-012), so a commit has an `api` tag, a `web` tag, both or neither. Roll each image back on its own: redeploy the previous sha that published it (the package's tag list, or the `publish` job summary of the earlier run), or pin by digest. Migrations are forward-only EF migrations; the design doc says changes should be forward-compatible, and no down-migration is tested. [UNVERIFIED for production.]

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
- **Ports and subnets**: `APP_PORT`, `AUTH_PORT`, `APP_SUBNET`, `DATA_SUBNET` (see `.env.example`). `DATA_SUBNET` is
  also the API's `Ports__InternalNetwork`: the management port answers only loopback and that network.
- **Back-channel logout check** (replace `dispute-portal` with your project name):
  - The edge never forwards it: `curl -s -o /dev/null -w '%{http_code}' -X POST -d logout_token=x
    http://localhost:8080/bff/backchannel-logout` prints `404` (nginx's own page).
  - Keycloak's path works: from the data network, `docker run --rm --network dispute-portal_data --entrypoint wget
    redis:8-alpine -q -S -O- --post-data logout_token=x http://api-mgmt:8081/bff/backchannel-logout` shows
    `400 Bad Request` (a refused token, so the endpoint is reachable and verifying) and the API logs
    `Back-channel logout token refused: Malformed`. From the app network (where nginx is), the management port
    answers 404 even with `Host: api-mgmt` and a forged `X-Forwarded-For`.
  - End to end: `e2e/tests/backchannel-logout.spec.ts` (a logout at Keycloak itself, and an admin deleting the
    session with `kcadm.sh`, each followed by a 401 on the very next request). It runs `docker compose exec
    keycloak`, so set `COMPOSE_PROJECT_NAME` when the stack is not the default project.
- Another stack using the same compose project name (`dispute-portal`) will share volumes with this one.

## 10. Data retention (design only)

The code has no retention job and no erasure endpoint. The design (`docs/design/...` section 5.5) says disputes and
history are kept for a regulator-driven period that is jurisdiction-dependent and an open assumption, transactions
follow the core-banking policy, the application role can never delete history, and erasure requests are met by
pseudonymising the customer display name while keeping structural records, subject to legal review. Keycloak keeps
login and admin events for 30 days (`eventsExpiration` 2592000 in the realm file). See the POPIA section of
[threat-model.md](threat-model.md).
