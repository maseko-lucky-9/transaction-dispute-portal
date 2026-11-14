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

- A fourth variable, `DP_FORWARDED_PROTO`, is the value of every `proxy_set_header X-Forwarded-Proto` (the `/api/` location, the `/bff/`, `/signin-oidc` and `/signout-callback-oidc` location, and both Keycloak locations on 8180). The Dockerfile default is the literal text `$scheme` (escaped as `\$scheme` in `ENV` so Docker does not expand it at build time; `NGINX_ENVSUBST_FILTER` keeps envsubst from touching it, so nginx sees `$scheme`). Compose passes nothing and the rendered config is byte-identical to before.
- Why: behind a TLS-terminating ingress the web nginx sees plain http, so `$scheme` tells the API and Keycloak the request was http and https login breaks. A cluster sets `DP_FORWARDED_PROTO=$http_x_forwarded_proto` to pass the ingress's header through.
- Trust rule: keep the default `$scheme` unless a trusted proxy is the only path to this nginx. Only then set `$http_x_forwarded_proto`; otherwise a client can send its own `X-Forwarded-Proto` and spoof the scheme. "Trusted" means all of:
  - the proxy sets or overwrites the header, it does not append to it or pass a client-supplied value through (ingress-nginx with `use-forwarded-headers: "true"` does pass a client value through when it is the first proxy, and an append yields a value such as `https, http`);
  - the web pods are reachable only through it (a NetworkPolicy, and no NodePort, LoadBalancer or second Service);
  - the setting applies to both origins (8080 and 8180), because the same variable feeds every location.
  If the header is missing the variable is empty for that request, `proxy_set_header` sends nothing and the API falls back to http. A spoofed value only misreports the scheme of the caller's own request (redirect URIs, cookie `Secure`); it cannot widen access.
- Like the other `DP_*` variables, an empty value renders an invalid config even when the other three are set (`proxy_set_header X-Forwarded-Proto ;`) and nginx exits at start.
