# 004. Backend-for-frontend authentication and session lifecycle

Status: accepted (2026-11-04)

## Context
Current OAuth guidance for browser apps keeps tokens out of JavaScript. Keycloak is the identity provider.

## Decision
- The ASP.NET Core API is the OIDC confidential client (authorization code + PKCE S256). The browser holds only an
  HttpOnly, Secure, SameSite=Lax `dp_session` cookie. The `__Host-` prefix is used only when the public URL is
  https, because Chrome rejects `__Host-` cookies on `http://localhost`.
- The cookie carries a ticket id. The ticket (id_token + refresh_token) lives in Redis, DataProtection-encrypted,
  under `bff:ticket:<id>`. The DataProtection keys are in Postgres, not Redis, so write access to Redis alone cannot
  forge a session. Redis runs a key-scoped ACL user, `noeviction` and AOF.
- Lifetime: 15 minutes idle, 8 hours absolute. When the access token is within 60 seconds of expiry the API
  refreshes it and re-reads roles; a failed refresh signs the user out. Revocation is bounded by the 5-minute access
  token. Back-channel logout is a stretch item.
- Logout is a top-level navigation to `/bff/logout?sid=...`, which deletes the ticket and calls Keycloak
  end-session with `id_token_hint`, so the next login asks for the password again (an E2E test proves it).
- Unauthenticated `/api` calls return JSON 401/403, never a redirect. The OIDC challenge happens only at
  `/bff/login`.
- Parallel requests on different replicas must not redeem the same refresh token (Keycloak rotation with reuse 0 would
  revoke the session). A Redis lock keyed by the token hash makes one replica refresh while the others wait, re-read the
  ticket and adopt the rotated tokens; a refusal deletes the ticket only if it still holds the refused token
  (compare-and-delete). An unreachable identity provider is a 503 `idp_unavailable` that keeps the session; only a
  refusal ends it.
- Signing in again revokes the previous ticket found in the incoming cookie.
- Redis down means auth endpoints return 503 (fail closed).
- A principal holding both `customer` and `dispute-agent` gets 403 `role_conflict` (segregation of duties).

## Consequences
No token is reachable from the page. Every request costs one Redis read. The per-replica rate limiter and the
ticket store are the only stateful pieces and both survive `--scale api=3`.
