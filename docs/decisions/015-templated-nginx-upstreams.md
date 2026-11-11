# ADR-015: nginx resolver and upstreams are rendered from a template

Status: accepted (2026-11-08). Prerequisite D0 of the per-half rollout (ADR-012).

## Context

`frontend/nginx/default.conf` hard-coded Docker's DNS (`resolver 127.0.0.11`) and the service names `api:8080` and `keycloak:8080`. The web image therefore could not run anywhere but compose.

## Decision

- The file is now `frontend/nginx/default.conf.template`. The base image's entrypoint renders `/etc/nginx/templates/*.template` into `conf.d` at start.
- Three variables: `DP_RESOLVER`, `DP_API_UPSTREAM`, `DP_KEYCLOAK_UPSTREAM`. Their defaults, set with `ENV` in the Dockerfile, are today's compose values, so compose passes nothing and behaviour is unchanged.
- `NGINX_ENVSUBST_FILTER=^DP_` limits substitution to those variables; nginx's own `$request_method`, `$rest` and the like are left alone.
- `conf.d` is written at start, so compose mounts it as a tmpfs owned by the nginx user (the service is `read_only`). A cluster does the same with an emptyDir.

## Consequences

- A cluster sets the three variables (the resolver is the cluster DNS service IP). Nothing else in the image changes.
- A non-empty but unreachable upstream still starts nginx and answers 502 for that route, as before. An empty variable renders an invalid config and nginx exits at start, so a deployment must set all three or none.
- Not covered: the CSP, cookie and Host handling assume the compose topology (two origins, ports 8080 and 8180). Ingress hostnames are a separate decision (Part D1).

## Addendum (D1c): `DP_FORWARDED_PROTO`

- A fourth variable, `DP_FORWARDED_PROTO`, is the value of every `proxy_set_header X-Forwarded-Proto` (the `/api/` and `/bff` locations and both Keycloak locations). The Dockerfile default is the literal text `$scheme` (escaped as `\$scheme` in `ENV` so Docker does not expand it at build time; `NGINX_ENVSUBST_FILTER` keeps envsubst from touching it, so nginx sees `$scheme`). Compose passes nothing and the rendered config is byte-identical to before.
- Why: behind a TLS-terminating ingress the web nginx sees plain http, so `$scheme` tells the API and Keycloak the request was http and https login breaks. A cluster sets `DP_FORWARDED_PROTO=$http_x_forwarded_proto` to pass the ingress's header through.
- Trust rule: keep the default `$scheme` unless a trusted proxy is the only path to this nginx. Only then set `$http_x_forwarded_proto`; otherwise a client can send its own `X-Forwarded-Proto` and spoof the scheme.
- Like the other `DP_*` variables, an empty value renders an invalid config (`proxy_set_header X-Forwarded-Proto ;`) and nginx exits at start.
