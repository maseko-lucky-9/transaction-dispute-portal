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

Reset everything (secrets, Vault, database): `docker compose down -v`. The secrets and the Postgres volume must be
reset together, because the database roles take their passwords from the secrets volume on first start.

## Build and test

```
dotnet build DisputePortal.slnx -warnaserror
dotnet test  tests/DisputePortal.UnitTests
dotnet test  tests/DisputePortal.IntegrationTests     # Testcontainers: needs Docker
npm --prefix web ci --ignore-scripts && npm --prefix web run lint typecheck test:coverage build
docker compose up -d --wait --build && npm --prefix e2e ci && npx --prefix e2e playwright test   # E2E against the real stack
```
