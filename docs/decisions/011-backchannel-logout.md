# 011. OpenID Connect Back-Channel Logout on the management port

Status: accepted (2026-11-04)

## Context
Until now a session ended at Keycloak (an admin signing the user out, the SSO idle or max timeout, a logout from another
application) lived on in the app until its next token refresh failed: up to 5 minutes, the access-token lifetime
(ADR-004). OpenID Connect Back-Channel Logout 1.0 lets Keycloak tell the client at once, by POSTing a signed
`logout_token` to a URL the client registered. That URL is unauthenticated by construction and deletes sessions, so
every check has to hold.

Constraints found in this stack: the API's public port sits behind nginx and validates `Host` (`AllowedHosts=localhost`);
CSRF (`X-CSRF: 1`) is required on every non-GET `/bff` request; the fallback policy requires a session. Keycloak reaches
the API only over the internal compose networks. Measured on Keycloak 26.4 (a token captured from the real server): the
logout token is RS256 with the realm key's `kid`, JWS `typ` `logout+jwt`, claims `iss` (the public URL), `aud` (our
client id), `iat`, `exp` = `iat` + 120 s, `jti`, `sub`, `sid`, `events`, and no `nonce`; the app's ID tokens carry the
same `sid` as Keycloak's session id.

## Decision
- **Endpoint.** `POST /bff/backchannel-logout`, `application/x-www-form-urlencoded`, one `logout_token` field, a
  declared `Content-Length` of at most 9 KB (form limits on top). Explicitly anonymous; excluded from the OpenAPI
  document (Keycloak's contract, not the SPA's; `docs/openapi.json` is unchanged).
- **Where it is served: the management port (8081) only.** It is the API's second Kestrel listener, never published,
  never proxied by nginx, already the home of the health probes. The endpoint carries `ManagementPortEndpoint` metadata;
  `ManagementPortMiddleware` matches the routed endpoint (not the raw path), so no spelling of the path (case, trailing
  slash, encoding) reaches it on the public port. nginx also answers 404 itself for `~* ^/bff/backchannel-logout`, so
  the public origin never forwards it.
- **Host.** Keycloak calls `http://api-mgmt:8081/bff/backchannel-logout`. `api-mgmt` is a compose alias of the API on the
  `data` network only (every replica answers to it). `Ports:InternalHost=api-mgmt` adds that one name to host filtering,
  and `ManagementPortMiddleware` answers 400 for it on the public port, so the public listener still accepts only
  `localhost`. The realm takes the URL from `${BACKCHANNEL_LOGOUT_URL}`, like its other placeholders.
- **Peers.** `Ports:InternalNetwork` (the data subnet) limits the management port to loopback (the container's own
  `--healthcheck`) and that network. nginx is on `edge` and `app` only, so even a compromised nginx gets 404.
  `X-Forwarded-*` is now applied on the public port only: on the management port the socket's peer is the caller, so a
  container on the trusted app network cannot claim a data-network address or another rate-limit partition.
- **CSRF.** The middleware exempts exactly an endpoint marked for the management port AND reached on the management
  port. Every public-port route, marked or not, still needs `X-CSRF: 1`. No browser can reach that port, and the signed
  token is the authentication.
- **Token validation** (`LogoutTokenValidator`, spec section 2.6) against the login handler's own
  `OpenIdConnectOptions`: its configuration manager (the same cached JWKS, no second trust root), its issuer
  (`Keycloak:Authority`, the public URL) and its client id. In order: length at most 8 KB; `alg` in {RS256} before any
  key is tried (so never `none`); JWS signature by a key of that set, matched by `kid` (an unknown `kid` asks the
  manager for fresh keys, throttled by the manager, and is refused); `typ` `logout+jwt` (an ID token signed by the same
  key cannot pass); `iss`; `aud` contains the client id; `iat` present, at most 2 minutes old and 30 s in the future;
  `exp` and `nbf` honoured when present; `events` holds the back-channel-logout member as a JSON object; no `nonce`;
  `jti` and `sid` present (the client sets `backchannel.logout.session.required`); `sub` optional. Only a token that
  passed every stateless check reaches Redis: `SET NX EX` on `bff:logout-jti:<sha256(iss, jti)>` for 3 minutes (the
  window in which its `iat` could still pass), so a replay to any replica is refused.
- **Responses.** Every refusal is the same 400 problem, `invalid_logout_token`, with one fixed detail: no oracle. A
  valid token for a session this API does not know (or already ended) is 200, as the spec requires. Redis down is 503
  `session_store_unavailable` (fail closed); Keycloak's metadata unavailable is 503 `idp_unavailable`. `no-store` on all.
- **Session index.** The ticket's principal holds the `sid`. Every ticket write also adds the ticket id to
  `bff:sid:<sha256(sid)>` in the same Redis transaction (a renewal of a ticket that is gone writes neither), with the
  TTL reset to the 8-hour absolute lifetime. Logout, sign-in revocation and compare-and-delete take the id out again.
  The back-channel deletes every ticket in the set and the set itself in one Lua script, so no ticket joins in between.
  Ids only: no token material, and the sid is hashed.
- **Abuse.** A per-peer limiter (60 a minute per replica, `RateLimiting:BackchannelLogoutsPerMinute`) on top of the
  global one; bounded work before Redis (size check, one RSA verification); fixed log lines that carry a reason name
  (`Back-channel logout token refused: Nonce`) or a count (`Back-channel logout ended 1 session(s)`), never a token, sid,
  sub or jti. `security.events{type}` gains `backchannel_logout` and `backchannel_logout_rejected` (bounded constants).
- **Keycloak client.** `frontchannelLogout: false`, `backchannel.logout.url`, `backchannel.logout.session.required:
  true`, `backchannel.logout.revoke.offline.tokens: false` (no offline tokens are issued).

## Alternatives
- **Serve it on the public port with a host allowance for this route.** Rejected: the route would sit behind nginx's
  `/bff/` proxy, one nginx regex away from the internet, and the public listener would gain a second accepted host.
- **A separate, third listener.** No gain over the management port, which is already unpublished and already split by
  local port; one more port to bind and document.
- **Path outside `/bff` to dodge the CSRF rule.** It would also lose the `no-store` rule and fall into the SPA's
  `try_files` fallback (a 200 HTML page) at the edge; an explicit, port-bound exemption is clearer.
- **`Host` = `localhost` from Keycloak.** Not possible: `localhost` inside the Keycloak container is Keycloak.
- **Front-channel logout (iframes).** Needs third-party cookies and a rendered page; back-channel is server to server.
- **Keep relying on the refresh.** Revocation stays bounded by 5 minutes; the refresh path remains as the fallback.

## Consequences
A session ended at Keycloak is dead in the app on the next request (E2E: a logout at Keycloak itself, and an admin
deleting the session through the admin API). Any replica may receive the POST; state is in Redis, so it is correct with
`--scale api=3` (Keycloak's HTTP client keeps one pooled connection and so mostly hits one replica; it fails over when
that replica goes away). Residual: Keycloak does not retry a failed delivery, so a back-channel call lost to an API or
Redis outage, a rate-limit 429 during a mass logout, or a key rotation before the API's metadata refresh falls back to
the 5-minute refresh bound; a sign-in completing at the same instant as its Keycloak session ends can leave a ticket
that the next refresh ends; Redis-down turns the call into 503, not a silent 200. A lost call is also quiet: Keycloak
26.4 logged nothing at its default level when the call failed (checked by pointing the client at a wrong path, which
also turned both E2E tests red).
