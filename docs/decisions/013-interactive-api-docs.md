# 013. Interactive API docs (Swagger UI) behind a flag, under /api

Status: accepted (2026-11-05). Replaces the "not in Production" rule for the OpenAPI document.

## Context
The OpenAPI document was mapped at `/openapi/v1.json` only outside Production. Compose runs the API as Production, so the
stack people actually run never showed it, and there was no UI. The browser reaches the API only through nginx, which
proxies `/api/`, `/bff/...`, `/signin-oidc` and `/signout-callback-oidc`; every other path falls to the SPA's `try_files`
and returns `index.html`. The `/api/` location also sends `csp-app.conf` (`script-src 'self'`).

## Decision
- `OpenApi:Enabled` (default **false in every environment**, Development included) maps both the document and the UI.
  Only the text `true` turns it on; unset, empty or garbled stays off and does not stop the host. Compose passes
  `OpenApi__Enabled: ${OPENAPI_ENABLED:-false}`, so `OPENAPI_ENABLED=true docker compose up` is the opt-in.
- The document is at `/api/openapi/v1.json` and the UI at `/api/swagger` (redirects to `/api/swagger/index.html`):
  under `/api` nginx already proxies them, with no new location. Its content is unchanged, so `docs/openapi.json` and the
  generated TS types are unchanged (the UI's own routes do not appear in it). The old `/openapi/v1.json` is not served.
- The UI is `Swashbuckle.AspNetCore.SwaggerUI` only; `Microsoft.AspNetCore.OpenApi` stays the sole document source.
- **CSP: no relaxation.** Swashbuckle 10.2.3's page has no inline script (three same-origin scripts; its config sits in
  `index.js`) and its validator is off. Checked in headless Chromium through nginx on the Compose stack, light and dark:
  the 14 operations render and no `securitypolicyviolation` event fires (a control inline script is reported, so the
  detector works). `csp-app.conf` and `frontend/nginx/` are untouched.
- **CSRF: a narrow exemption.** `CsrfMiddleware` requires `X-CSRF: 1` on every `/api` request, and a browser cannot add a
  header to a page load or a script tag, so without it the UI itself is a 403. A GET or HEAD of an endpoint carrying the
  `ApiDocumentationEndpoint` marker (only the document and the UI files, only when the flag is on) is exempt. With the flag
  off nothing carries the marker, so the paths meet the same CSRF check and the same fallback policy as any unknown `/api`
  path (403 without the header, 401 with it, 404 once signed in), which does not reveal that the UI exists.

## Consequences
- Production exposes the document and the UI only by an explicit flag.
- The UI assets are `Cache-Control: no-store` (`NoStoreMiddleware` covers all of `/api`); harmless for a dev tool.
- "Try it out" is limited. Auth is the BFF `dp_session` cookie plus the CSRF header, not a bearer token, and no bearer
  scheme is added. After signing in at `/bff/login` in the same browser the cookie is sent, so `GET /bff/user` returns 200.
  A `/api/*` call still returns 403 `csrf_required`: the document declares the `csrf` scheme but attaches it to no
  operation, so Swagger UI never sends `X-CSRF`, and Authorize does not change that. Reading the contract works; calling
  `/api/*` from the UI would need a security requirement in the document or a same-origin script that sets the header (a
  request-interceptor string needs `unsafe-eval`, which the CSP forbids). Not done here.
- Rejected: loosening `csp-app.conf` (not needed); having nginx add `X-CSRF` for the docs (the API would then trust a
  proxy-set header for the rule it exists to enforce); a Development-only UI (never visible in the Compose stack).
