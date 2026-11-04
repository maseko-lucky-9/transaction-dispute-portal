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
  auth server block proxies an end-anchored allow-list (`login-actions`, and the openid-connect `auth`, `logout`,
  `logout/logout-confirm`, `login-status-iframe.html` and `3p-cookies` pages) plus `/resources/`; token, certs,
  discovery, account console, client registration, admin console, master realm and metrics are 404 there. nginx's
  own error pages on that origin carry a deny-all CSP. It strips the app cookie (also the `__Host-` form, in first, middle and last position), because cookies are not
  port-scoped. Keycloak
  keeps its own CSP.
- `Cache-Control: no-store` on `/api` and `/bff`; hashed assets are immutable; `index.html` is `no-cache`.
- HSTS is not sent by this stack: the local and CI stack serves plain http, and neither `web/nginx` nor the API sets
  `Strict-Transport-Security`. A production deployment must add it at the TLS-terminating edge. It is not counted as a
  control here (see the threat model). Corrected 2026-11-04: this line previously said HSTS was sent over https, which
  the code does not do.

## Consequences
One more origin to document. In exchange the admin console and master realm are unreachable from the browser.
