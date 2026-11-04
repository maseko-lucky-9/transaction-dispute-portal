# 008. Content-Security-Policy, edge headers and a separate Keycloak origin

Status: accepted (2026-11-04)

## Decision
- nginx serves the SPA with `default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self'
  data:; font-src 'self'; connect-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none';
  object-src 'none'`. `'unsafe-inline'` applies to styles only, because Radix primitives inject runtime styles;
  script-src stays strict. A Playwright check fails on any CSP violation.
- Security headers are `include`d in every location (nginx drops server-level `add_header` once a location sets its
  own), and tested per location.
- Keycloak is on its own origin (`AUTH_PORT`), so XSS on the app cannot reach its pages and cookies stay apart. The
  auth server block allows only `/realms/dispute-portal/` and `/resources/` and returns 404 for everything else
  (admin console, master realm, metrics). It strips the app cookie, because cookies are not port-scoped. Keycloak
  keeps its own CSP.
- `Cache-Control: no-store` on `/api` and `/bff`; hashed assets are immutable; `index.html` is `no-cache`.
- HSTS is sent only over https and is not counted as a control on localhost.

## Consequences
One more origin to document. In exchange the admin console and master realm are unreachable from the browser.
