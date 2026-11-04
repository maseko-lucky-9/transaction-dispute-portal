# 005. Vault AppRole for application credentials, and the "secret zero" caveat

Status: accepted (2026-11-04)

## Context
The brief asks for Vault-held application credentials. A local compose stack still needs some trusted party to
hand the first credential to each service.

## Decision
- Vault runs in server mode with file storage (not `-dev`, which loses everything on restart). One key share:
  acceptable for local use only; production would auto-unseal and keep no root token.
- A one-shot `bootstrap` container is idempotent. It generates any missing secret into a `secrets` volume,
  initialises or unseals Vault, writes KV v2 `kv/dispute-portal/{database,database-owner,redis,keycloak}`, writes
  two policies and two AppRoles: `dispute-api` (runtime credentials) and `dispute-migrate` (schema owner only).
  secret_ids are CIDR-bound to the compose network and expire after 24 hours.
- The API and the migrate job read credentials only from Vault at startup, with bounded backoff, then exit with a
  one-line hint. Secrets are read once; rotation is a rolling restart. The upgrade path is Vault dynamic database
  credentials.
- Postgres, Redis and Keycloak cannot talk to Vault, so they read their own sub-path of the secrets volume,
  mounted read-only.

## Consequences
Honest limitation: locally, `bootstrap` is the trusted orchestrator that delivers secret zero, and anyone who can
read the `secrets` volume owns the stack. In production, platform identity (for example Kubernetes auth) replaces
AppRole file delivery.
