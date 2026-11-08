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
