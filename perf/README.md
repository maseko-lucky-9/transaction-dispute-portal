# perf: k6 p95 smoke test

`k6/smoke.js` signs in through the real BFF and Keycloak flow, then runs a read-heavy mix plus a low-rate submit/withdraw
loop against a running stack, and fails (exit 99) if read p95 is 300 ms or more, write p95 is 500 ms or more, more than
1% of requests fail, or any request gets a 429. Method, results and caveats: [`docs/perf.md`](../docs/perf.md).

Run from the repository root. Needs only Docker.

```
# 1. An isolated stack (3 API replicas). The inline override only tags the images :perf so the default
#    project's dispute-portal-*:local images are not retagged. Adjust the subnets if they clash.
printf 'services:\n  migrate: { image: dispute-portal-api:perf }\n  api: { image: dispute-portal-api:perf }\n  web: { image: dispute-portal-web:perf }\n' \
  | APP_PORT=8880 AUTH_PORT=8980 DATA_SUBNET=10.231.68.0/24 APP_SUBNET=10.231.69.0/24 \
    docker compose -p dp-perf -f docker-compose.yml -f - up --build -d --wait --scale api=3

# 2. The test, on the stack's edge network (exit code 0 = all thresholds held)
docker run --rm --network dp-perf_edge -v ./perf/k6:/scripts:ro \
  grafana/k6:2.3.0@sha256:2dd4756ec6e14e62778916e1eb850108e4b499304c02df56760fa0bdb0fba0b8 \
  run --quiet --no-color /scripts/smoke.js

# 3. Tear down (deletes only the dp-perf project and its volumes)
docker compose -p dp-perf down -v
```

Knobs, passed as `-e NAME=value` before the image name: `DURATION` (default `3m`), `READ_PER_MIN` (per user, default
240), `WRITE_PER_MIN` (submit and withdraw pairs per customer, default 3), `READ_P95_MS`, `WRITE_P95_MS`.
If you changed `APP_PORT` or `AUTH_PORT`, also pass `-e PUBLIC_APP=http://localhost:<APP_PORT> -e PUBLIC_AUTH=http://localhost:<AUTH_PORT>`.

If a run reports any `http_429`, it is invalid: lower `READ_PER_MIN` and rerun. Do not raise the rates to "stress" the
stack; the per-subject limits (300 requests per minute, 10 submits per minute, per replica) cap what one user can send.
