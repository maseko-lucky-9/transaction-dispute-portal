# Transactions Dispute Portal

Customers view their bank transactions and dispute them, with a historic view of every dispute.

> Work in progress for the SE3 take-home. The full README (criteria → evidence table, test catalogue,
> threat model, runbook) lands in the final PR. This section is what you need to run it today.

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
those two variables. Use Chromium or Firefox for the local demo (Safari's handling of `Secure` cookies on
`http://localhost` is unverified).

Demo users (local-only, imported from `deploy/keycloak/`):

| User | Password | Role |
|---|---|---|
| `thandi` | `Thandi-Demo-2026!` | customer |
| `sipho` | `Sipho-Demo-2026!` | customer |
| `agent` | `Agent-Demo-2026!` | dispute-agent |

The two internal networks use `10.231.28.0/24` (data) and `10.231.29.0/24` (app); set `DATA_SUBNET` / `APP_SUBNET` if
they clash with a VPN or another Docker network. Compose never gives `web` (the internet-facing container) a route to
Postgres, Redis or Vault.

Reset everything (secrets, Vault, database): `docker compose down -v`. The secrets and the Postgres volume must be
reset together, because the database roles take their passwords from the secrets volume on first start.

## Build and test

```
dotnet build DisputePortal.slnx -warnaserror
dotnet test  tests/DisputePortal.UnitTests
dotnet test  tests/DisputePortal.IntegrationTests     # Testcontainers: needs Docker
dotnet build DisputePortal.slnx -c Release && scripts/coverage.sh   # both suites merged, fails below the line-coverage floor
dotnet tool restore && (cd tests/DisputePortal.UnitTests && dotnet tool run dotnet-stryker)   # mutation score of Domain, report only
npm --prefix web ci --ignore-scripts && npm --prefix web run lint && npm --prefix web run typecheck \
  && npm --prefix web run test:coverage && npm --prefix web run build
docker compose up -d --wait --build && npm --prefix e2e ci && npx --prefix e2e playwright test   # E2E against the real stack
```

## Known limits (deliberate, with the upgrade path)

| Limit | Why it is acceptable here | Upgrade path |
|---|---|---|
| Rate limits are per API replica | N replicas allow about N times the quota | Redis-backed limiter |
| Vault runs with one key share, and `bootstrap` plays the trusted orchestrator that delivers each service's AppRole files ("secret zero") | Local development only; the AppRoles are bound to the `data` network, which `web` is not on | Auto-unseal, no root token, platform identity (for example Kubernetes auth) instead of files |
| Issued AppRole secret_ids stay valid for 24 h (tidy removes only expired ones) | Bounded by the network binding and the TTL | Single-use secret_ids with response wrapping |
| DataProtection keys sit unencrypted in Postgres, guarded by grants | A Redis write alone cannot forge a session | `ProtectKeysWithCertificate` with a key held in Vault |
| Keycloak and the data stores are not `read_only` | They need writable state; capabilities are dropped | Per-image tmpfs mounts |
| Database credentials are read once at start-up | Rotation means a rolling restart | Vault dynamic database credentials |
| The 20-active and 3-per-transaction dispute caps can be overshot by concurrent submits | They limit abuse; they are not money invariants. One active dispute per transaction is a unique index | An advisory lock around count and insert |
| A caller can learn that a client-chosen dispute id exists (409 versus 201), though never its content | The id is random, 122 bits, seen only by its owner, and submits are limited to 10 a minute | Scope the idempotency key per customer |
| The app's database role can change a dispute's own columns without a matching history row (history itself is append-only) | It needs a stolen app credential; grants are column-scoped | A terminal-state guard trigger and DB-written history |
| The card and ID number check deters accidental disclosure, not deliberate obfuscation by the customer about their own number (five or more separators, words between digit groups, invisible non-format code points such as variation selectors, circled or superscript digits, fullwidth punctuation as separators) | Free text is also never logged; PCI scope is documented in the threat model | None without a tokenisation service |
| "Latest dispute" ordering uses the replica's clock | Replicas share NTP; the effect is one wrong button for a re-dispute within the skew | A database-stamped `created_at` |
