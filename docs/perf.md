# Performance smoke test (k6)

The runbook's SLOs (read p95 at most 300 ms, write p95 at most 500 ms, 99.9% availability) were targets with nothing
measuring them. `perf/k6/smoke.js` is the first measurement. It is a **smoke test on a laptop**, not a capacity test;
read "What this does and does not prove" before quoting any number.

## Result

Two identical 3-minute runs against a freshly built 3-replica stack, 2026-11-04. Every threshold held in both.
Latencies are client-side (k6 `timings.duration`: time to first byte plus body, no connect), in milliseconds.

| Group | Run | Requests | req/s | p50 | p95 | p99 | max |
|---|---|---|---|---|---|---|---|
| Reads (`GET /api/v1/*`) | 1 | 2163 | 12.00 | 6.36 | 8.66 | 10.38 | 32.9 |
| Reads | 2 | 2162 | 12.00 | 5.53 | 8.27 | 10.41 | 17.3 |
| Writes (submit + withdraw) | 1 | 38 | 0.21 | 6.84 | 17.78 | 22.89 | 25.3 |
| Writes | 2 | 40 | 0.22 | 9.43 | 20.93 | 23.21 | 23.3 |

| | Run 1 | Run 2 |
|---|---|---|
| Failures (`http_req_failed`, `errors`) | 0 of 2233, 0 of 2201 | 0 of 2234, 0 of 2202 |
| HTTP 429 (`http_429`) | 0 | 0 |
| Dropped iterations (generator kept the planned rate) | 0 | 0 |
| k6 exit code | 0 | 0 (the 20 s repeat of the documented command also gave 0) |

Per endpoint (p95, run 1 / run 2, ms): tx list 9.25 / 9.43, tx detail 8.71 / 7.81, disputes list 8.00 / 6.92, dispute
detail 8.23 / 7.50, agent queue 6.87 / 6.56, agent detail 7.64 / 7.34, submit 17.72 / 23.03, withdraw 8.37 / 9.09.

**Run-to-run stability.** Read p95 went 8.66 to 8.27 ms (-4%). Write p95 went 17.78 to 20.93 ms (+18%, inside the 20% bound
but close to it). The write figure is the 95th percentile of only 38 and 40 samples, so it is the 2nd-highest sample in each
run; the difference is one slow submit (the submit p95 moved 17.7 to 23.0 ms, the withdraw p95 8.4 to 9.1 ms). It is noise
at this sample size, not a trend. The reads, with 2000+ samples, are stable.

**Against the SLOs.** Read p95 8 ms against a 300 ms target and write p95 18 to 21 ms against 500 ms: both met with more
than an order of magnitude of headroom, *at 12 requests per second*. Availability (99.9%) is not measured by this test: 0
failures in about 4,400 requests only says no failure showed up in six minutes, and a 99.9% claim needs weeks of traffic.
The runbook should keep calling availability a target.

## What this does and does not prove

Proves:

- The full path works end to end under repeated load: nginx, 3 API replicas, Redis sessions, Postgres, real OIDC sign-in,
  keyset paging, ETag/If-Match withdraw. No 5xx, no 429, no unexpected status in about 4,400 requests.
- Session handling works across replicas: one cookie is accepted by whichever replica nginx picks (the `--scale api=3`
  path the README says no test exercised).
- The thresholds really gate: see the negative control below.

Does not prove:

- **Capacity or the SLO under real load.** The rate is capped by design. The API limits each user to 300 requests per
  minute per replica, and the script keeps each of the three users at 240 per minute so that a worst-case spread over
  replicas still gives zero 429s. That is 12 requests per second in total; the CPU of each API replica sat at about 10%
  during the run (`docker stats` below). A p95 of 8 ms at 10% CPU says nothing about where latency turns bad. Finding that
  needs many more distinct users (the limit is per subject) or a raised limit.
- **Server duration.** The SLO is defined on server duration; k6 measures what a client sees, including nginx and the
  Docker network. The server-side number is smaller.
- **Production latency.** The client ran in a container on the same Docker VM network as the stack, so the Mac's NAT and any
  real network are not in the path, and nothing here includes TLS, a CDN, or a browser. The database is tiny: about 150
  transactions per customer, a few dozen disputes. Keyset paging is an index scan so it should scale, but this test does not
  show that (`TransactionPlanTests` checks the plan on 50k rows).
- **A cold start.** There is no warm-up phase, so JIT and connection-pool start-up are inside the numbers (they showed up as
  p99 up to 150 ms in an earlier 20 s dry run, not in the 3-minute runs).
- **Availability, soak behaviour, memory growth, token refresh storms** (sessions live 15 minutes idle and access tokens 5
  minutes, so a few refreshes happen mid-run, but not at volume).
- **The agent write path.** Transitions (`POST /agent/disputes/{id}/transitions`) are not exercised; agent traffic is reads only.
- **Whether the 429 threshold fires.** No 429 appeared, so the counter was never seen incrementing. [UNVERIFIED]

## Method

- **Tool:** `grafana/k6:2.3.0` pinned by digest `sha256:2dd4756ec6e14e62778916e1eb850108e4b499304c02df56760fa0bdb0fba0b8`
  (the multi-arch index digest, looked up with `docker buildx imagetools inspect`; `latest` at the time). No host install.
- **Target:** an isolated compose project `dp-perf` built from this worktree (base commit `0e882b3`), `--scale api=3`,
  ports 8880 and 8980, subnets 10.231.68.0/24 (data) and 10.231.69.0/24 (app). k6 runs in a container on the project's
  `edge` network and talks to `web:8080` (the app origin) and `web:8180` (the Keycloak origin), the same nginx a browser
  uses. It sends `Host: localhost:8880` / `localhost:8980` and maps the OIDC redirects back, because the API accepts
  only `Host: localhost` and Keycloak issues redirect URIs from `APP_PORT` and `AUTH_PORT`.
- **Sign-in (setup):** for `thandi`, `sipho` and `agent`: `GET /bff/login`, follow the redirect to Keycloak, parse
  `form#kc-form-login` with `k6/html`, post the credentials, follow `/signin-oidc` back, and keep the `dp_session` cookie.
  The BFF marks its cookies `Secure`, which k6's built-in jar will not send over http, so the script keeps a small manual
  jar (the same thing a browser does for `http://localhost`). Every API call carries the cookie and `X-CSRF: 1`.
  Setup also walks each customer's transactions (ids, cursors, and the set of disputable ones) and makes sure each
  customer has 3 open disputes to read; those stay open.
- **Reads (one request per iteration, 240 per minute per user):** customers: 45% `GET /transactions?limit=25` with a
  random cursor from the walked pages, 30% `GET /transactions/{id}`, 13% `GET /disputes`, 12% `GET /disputes/{id}`.
  Agent: 50% `GET /agent/disputes` (queue), 50% `GET /agent/disputes/{id}` (audited view).
- **Writes (3 per minute per customer):** `POST /disputes` with a client-generated UUID on a random disputable
  transaction, then `POST /disputes/{id}/withdraw` with the `If-Match` ETag from the 201. The dispute is withdrawn so the
  20-active-disputes cap is not approached; the transaction can be disputed again (cap: 3 per transaction, ~100 candidates).
- **Rate plan (sized to stay under the limits):** the limiter is a fixed one-minute window per subject per replica, and nginx
  may send everything of one user to one replica. Per user: 240 reads + 6 writes + at most 40 setup requests is under
  300; submits are 3 per minute + 3 in setup, under 10. The script throws at start-up if the plan breaks either limit.
  Values must be integers (a typo such as `READ_PER_MIN=abc` stops the run), and setup fails if it needs more than the 40
  requests the plan assumes. A 429 aborts the run at once (`http_429` count must be 0, `abortOnFail`); a run with any 429 is
  invalid and must be repeated lower. The abort path was added after review and has not been seen firing against a live
  stack. [UNVERIFIED]
- **Thresholds (in the script):** `read_duration p(95)<300`, `write_duration p(95)<500`, `errors rate<0.01`,
  `http_req_failed rate<0.01`, `http_429 count==0`, `dropped_iterations count==0`. `read_duration` and `write_duration` are
  custom trends fed only by the measured calls (setup and sign-in are excluded); there is one trend per endpoint too.
- **Order:** stack up and idle, 20 s dry run (to check the script), run 1, run 2, a 20 s negative control, a 20 s repeat of the
  documented command.

## Environment

| | |
|---|---|
| Machine | Apple M5 Pro, 15 cores, 24 GiB RAM, macOS (Darwin 27) |
| Docker | 29.9.2, Docker Desktop; VM limits 15 CPUs, 11.67 GiB |
| Other load | Quiet: one unrelated idle container (`reelsmith-postgres-1`, ~0% CPU) |
| Stack | vault 1.20, postgres 17, redis 8, keycloak 26.4, 3 x API (.NET 10, `ASPNETCORE_ENVIRONMENT=Production`), nginx web; no CPU or memory limits set |
| Dataset | seeded by migrate: about 150 transactions per customer |

`docker stats --no-stream` (CPU % / memory), taken with the stack idle before run 1, during run 2 (about a minute in), and
idle after run 2:

| Container | Before run 1 (idle) | During run 2 | After run 2 |
|---|---|---|---|
| api-1 | 0.75% / 128 MiB | 10.2% / 158 MiB | 0.71% / 128 MiB |
| api-2 | 0.66% / 125 MiB | 10.1% / 156 MiB | 0.66% / 123 MiB |
| api-3 | 0.54% / 124 MiB | 9.4% / 153 MiB | 0.72% / 131 MiB |
| web (nginx) | 0.02% / 14 MiB | 0.4% / 15 MiB | 0.00% / 14 MiB |
| keycloak | 0.64% / 616 MiB | 0.5% / 624 MiB | 0.67% / 626 MiB |
| postgres | 0.01% / 63 MiB | 1.8% / 68 MiB | 0.00% / 70 MiB |
| redis | 0.28% / 12 MiB | 0.3% / 14 MiB | 0.25% / 13 MiB |
| vault | 0.47% / 42 MiB | 0.5% / 41 MiB | 0.52% / 43 MiB |

(The "before" column was taken after a 20 s dry run, with the stack otherwise idle.)

## Negative control: do the thresholds gate?

Same script, 20 s, with the read threshold set absurdly low through the documented knob (`-e READ_P95_MS=1`):

```
    read_duration
    ✗ 'p(95)<1' p(95)=8.95ms
    ...
time="2026-11-04T17:57:57Z" level=error msg="thresholds on metrics 'read_duration' have been crossed"
exit=99
```

Exit code 99, k6's "thresholds crossed" code, so a CI step or shell `&&` stops on a breach.

## Raw k6 output

Run 1 (`exit=0`; `--quiet --no-color`; progress lines omitted):

```
  █ THRESHOLDS 

    dropped_iterations
    ✓ 'count==0' count=0

    errors
    ✓ 'rate<0.01' rate=0.00%

    http_429
    ✓ 'count==0' count=0

    http_req_failed
    ✓ 'rate<0.01' rate=0.00%

    read_duration
    ✓ 'p(95)<300' p(95)=8.66ms

    write_duration
    ✓ 'p(95)<500' p(95)=17.78ms


  █ TOTAL RESULTS 

    checks_total.......: 22      0.12206/s
    checks_succeeded...: 100.00% 22 out of 22
    checks_failed......: 0.00%   0 out of 22

    ✓ thandi has the expected role
    ✓ sipho has the expected role
    ✓ agent has the expected role
    ✓ withdrawn dispute is Withdrawn

    CUSTOM
    ep_agent_detail................: avg=5.82ms  min=2.07ms   med=5.94ms  p(90)=7.06ms  p(95)=7.64ms  p(99)=8.74ms  max=16.33ms
    ep_agent_queue.................: avg=5.4ms   min=1.82ms   med=5.45ms  p(90)=6.44ms  p(95)=6.87ms  p(99)=9.44ms  max=32.36ms
    ep_dispute_detail..............: avg=6.35ms  min=2.33ms   med=6.39ms  p(90)=7.66ms  p(95)=8.23ms  p(99)=9.29ms  max=31.88ms
    ep_disputes_list...............: avg=5.82ms  min=2.13ms   med=5.93ms  p(90)=6.87ms  p(95)=8ms     p(99)=8.67ms  max=10.66ms
    ep_submit......................: avg=13.83ms min=6.7ms    med=14.03ms p(90)=17.48ms p(95)=17.72ms p(99)=18.58ms max=18.79ms
    ep_tx_detail...................: avg=6.74ms  min=2.25ms   med=6.86ms  p(90)=8.15ms  p(95)=8.71ms  p(99)=10.23ms max=32.9ms 
    ep_tx_list.....................: avg=7.21ms  min=3.05ms   med=7.38ms  p(90)=8.71ms  p(95)=9.25ms  p(99)=10.88ms max=18.44ms
    ep_withdraw....................: avg=6.62ms  min=4.38ms   med=5.52ms  p(90)=6.34ms  p(95)=8.37ms  p(99)=21.91ms max=25.3ms 
    errors.........................: 0.00%  0 out of 2201
    http_429.......................: 0      0/s
    read_duration..................: avg=6.38ms  min=1.82ms   med=6.36ms  p(90)=8.11ms  p(95)=8.66ms  p(99)=10.38ms max=32.9ms 
    read_requests..................: 2163   12.00067/s
    write_duration.................: avg=10.22ms min=4.38ms   med=6.84ms  p(90)=16.78ms p(95)=17.78ms p(99)=22.89ms max=25.3ms 
    write_requests.................: 38     0.21083/s

    HTTP
    http_req_duration..............: avg=6.45ms  min=109.58µs med=6.35ms  p(90)=8.19ms  p(95)=8.82ms  p(99)=13.29ms max=32.9ms 
      { expected_response:true }...: avg=6.45ms  min=109.58µs med=6.35ms  p(90)=8.19ms  p(95)=8.82ms  p(99)=13.29ms max=32.9ms 
    http_req_failed................: 0.00%  0 out of 2233
    http_reqs......................: 2233   12.389042/s

    EXECUTION
    dropped_iterations.............: 0      0/s
    iteration_duration.............: avg=6.91ms  min=1.98ms   med=6.8ms   p(90)=8.63ms  p(95)=9.23ms  p(99)=13.36ms max=45.61ms
    iterations.....................: 2182   12.106085/s
    vus............................: 0      min=0         max=0 
    vus_max........................: 16     min=16        max=16

    NETWORK
    data_received..................: 10 MB  56 kB/s
    data_sent......................: 1.2 MB 6.7 kB/s
```

Run 2 (same command; `exit=0`):

```
  █ THRESHOLDS 

    dropped_iterations
    ✓ 'count==0' count=0

    errors
    ✓ 'rate<0.01' rate=0.00%

    http_429
    ✓ 'count==0' count=0

    http_req_failed
    ✓ 'rate<0.01' rate=0.00%

    read_duration
    ✓ 'p(95)<300' p(95)=8.27ms

    write_duration
    ✓ 'p(95)<500' p(95)=20.93ms


  █ TOTAL RESULTS 

    checks_total.......: 23      0.127611/s
    checks_succeeded...: 100.00% 23 out of 23
    checks_failed......: 0.00%   0 out of 23

    ✓ thandi has the expected role
    ✓ sipho has the expected role
    ✓ agent has the expected role
    ✓ withdrawn dispute is Withdrawn

    CUSTOM
    ep_agent_detail................: avg=5.13ms  min=1.96ms   med=5.02ms  p(90)=6.69ms  p(95)=7.34ms  p(99)=9.64ms  max=11.04ms
    ep_agent_queue.................: avg=4.59ms  min=1.87ms   med=4.59ms  p(90)=5.98ms  p(95)=6.56ms  p(99)=7.77ms  max=9.03ms 
    ep_dispute_detail..............: avg=5.73ms  min=2.35ms   med=5.85ms  p(90)=7.2ms   p(95)=7.5ms   p(99)=8.52ms  max=9.05ms 
    ep_disputes_list...............: avg=5.02ms  min=1.91ms   med=5.21ms  p(90)=6.61ms  p(95)=6.92ms  p(99)=7.99ms  max=9.77ms 
    ep_submit......................: avg=15.41ms min=5.06ms   med=16.28ms p(90)=21.04ms p(95)=23.03ms p(99)=23.28ms max=23.34ms
    ep_tx_detail...................: avg=5.8ms   min=2.41ms   med=5.58ms  p(90)=7.31ms  p(95)=7.81ms  p(99)=10.4ms  max=11.49ms
    ep_tx_list.....................: avg=6.61ms  min=2.46ms   med=6.62ms  p(90)=8.58ms  p(95)=9.43ms  p(99)=11.16ms max=17.27ms
    ep_withdraw....................: avg=6.19ms  min=3.4ms    med=5.94ms  p(90)=8.89ms  p(95)=9.09ms  p(99)=11.6ms  max=12.23ms
    errors.........................: 0.00%  0 out of 2202
    http_429.......................: 0      0/s
    read_duration..................: avg=5.66ms  min=1.87ms   med=5.53ms  p(90)=7.71ms  p(95)=8.27ms  p(99)=10.41ms max=17.27ms
    read_requests..................: 2162   11.995452/s
    write_duration.................: avg=10.8ms  min=3.4ms    med=9.43ms  p(90)=17.86ms p(95)=20.93ms p(99)=23.21ms max=23.34ms
    write_requests.................: 40     0.221933/s

    HTTP
    http_req_duration..............: avg=5.76ms  min=111.37µs med=5.53ms  p(90)=7.78ms  p(95)=8.61ms  p(99)=12.24ms max=49.31ms
      { expected_response:true }...: avg=5.76ms  min=111.37µs med=5.53ms  p(90)=7.78ms  p(95)=8.61ms  p(99)=12.24ms max=49.31ms
    http_req_failed................: 0.00%  0 out of 2234
    http_reqs......................: 2234   12.394931/s

    EXECUTION
    dropped_iterations.............: 0      0/s
    iteration_duration.............: avg=6.14ms  min=2ms      med=5.9ms   p(90)=8.2ms   p(95)=9.05ms  p(99)=12.66ms max=36.95ms
    iterations.....................: 2182   12.106419/s
    vus............................: 0      min=0         max=0 
    vus_max........................: 16     min=16        max=16

    NETWORK
    data_received..................: 11 MB  62 kB/s
    data_sent......................: 1.2 MB 6.7 kB/s
```

(`http_reqs` and `http_req_failed` include the 32 setup requests: sign-in hops and the id walk.)

## Rerun

From the repository root; needs only Docker (Compose v2.27+). The first command builds the images (a few minutes cold).

```
# 1. Isolated stack, 3 API replicas; wait for healthy. Pick free subnets with `docker network ls` if these clash.
printf 'services:\n  migrate: { image: dispute-portal-api:perf }\n  api: { image: dispute-portal-api:perf }\n  web: { image: dispute-portal-web:perf }\n' \
  | APP_PORT=8880 AUTH_PORT=8980 DATA_SUBNET=10.231.68.0/24 APP_SUBNET=10.231.69.0/24 \
    docker compose -p dp-perf -f docker-compose.yml -f - up --build -d --wait --scale api=3

# 2. Run the test; run it twice and compare read/write p95 (exit code 0 = thresholds held, 99 = a threshold failed)
docker run --rm --network dp-perf_edge -v ./perf/k6:/scripts:ro \
  grafana/k6:2.3.0@sha256:2dd4756ec6e14e62778916e1eb850108e4b499304c02df56760fa0bdb0fba0b8 \
  run --quiet --no-color /scripts/smoke.js; echo "exit=$?"

# 3. Negative control (must exit 99)
docker run --rm --network dp-perf_edge -v ./perf/k6:/scripts:ro -e DURATION=20s -e READ_P95_MS=1 \
  grafana/k6:2.3.0@sha256:2dd4756ec6e14e62778916e1eb850108e4b499304c02df56760fa0bdb0fba0b8 \
  run --quiet --no-color /scripts/smoke.js; echo "exit=$?"

# 4. Remove only this project and its volumes
docker compose -p dp-perf down -v
```

Notes:

- The inline override in step 1 only gives the built images the tag `:perf`, so the default `dispute-portal` project's
  `dispute-portal-*:local` images are not retagged by a perf build.
- [UNVERIFIED, other ports not tried] With other ports, also pass `-e PUBLIC_APP=http://localhost:<APP_PORT> -e PUBLIC_AUTH=http://localhost:<AUTH_PORT>` to
  step 2, and keep the `dp-perf` stack on volumes that match its ports (the realm is imported once, with the redirect URIs
  of the first start; if `APP_PORT` changes, `down -v` first).
- Each run leaves 3 open disputes per customer and some withdrawn ones. Reruns on the same stack are fine; `down -v` resets.
- If any run shows `http_429` above 0, discard it and rerun with a lower `-e READ_PER_MIN=...`.
- The script posts the demo passwords and a session cookie to whatever it is pointed at, so it refuses any origin that is
  not `web`, `localhost` or `127.0.0.1` unless `ALLOW_NONLOCAL=1` is set, and `PUBLIC_APP` / `PUBLIC_AUTH` must look like
  `http://localhost:<port>`. Do not point it at a shared or production service. The `edge` network is not `internal`, so the
  container has internet egress. Only the local compose stack was tried.

## Follow-ups (not done)

- Wire the script into CI as an optional job once someone decides what a CI runner's numbers are worth (shared runners are noisier than this laptop).
- A capacity run needs more users than three (the limits are per subject) or a raised `RateLimiting__PermitsPerMinute`
  on a throwaway stack. Both are separate experiments.
- Add `/agent/.../transitions` to the write mix, and a warm-up phase.
- Update the runbook's SLO paragraph ("nothing measures them") to point here. Not done in this change by instruction.
