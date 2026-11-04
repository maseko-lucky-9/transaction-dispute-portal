// k6 p95 smoke test for the dispute portal. See docs/perf.md for method, caveats and exact commands.
//
// It signs in through the real BFF -> Keycloak OIDC flow in setup() (no token shortcuts), then drives a read-heavy mix
// plus a low-rate write loop through the same nginx the browser uses. Thresholds gate the run (exit code 99 on breach).
//
// Rate design (the API limits are per replica, per subject, fixed 1-minute window: 300 requests overall, 10 on
// POST /api/v1/disputes): nginx spreads requests over the replicas, so the worst case is every request of one subject
// landing on one replica. The script therefore keeps EACH subject under that worst case on its own:
//   reads 240/min + writes 2 x 3/min + setup < 270 < 300, and submits 3/min + setup 3 < 10.
// Any 429 makes the run invalid (threshold on the counter): rerun at lower READ_PER_MIN, never ignore it.
import http from 'k6/http';
import { check, fail } from 'k6';
import { parseHTML } from 'k6/html';
import { Counter, Rate, Trend } from 'k6/metrics';

// ---- configuration (all overridable with `-e NAME=value`) -------------------------------------------------------
const num = (name, fallback) => (__ENV[name] === undefined ? fallback : Number(__ENV[name]));
// Where k6 reaches the stack (inside the compose edge network) ...
const APP_ORIGIN = __ENV.APP_ORIGIN || 'http://web:8080';
const AUTH_ORIGIN = __ENV.AUTH_ORIGIN || 'http://web:8180';
// ... and the public origins the stack was configured with (APP_PORT / AUTH_PORT). The API only accepts Host:
// localhost, and the OIDC redirects carry these origins, so we send them as Host and map them back when following.
const PUBLIC_APP = __ENV.PUBLIC_APP || 'http://localhost:8880';
const PUBLIC_AUTH = __ENV.PUBLIC_AUTH || 'http://localhost:8980';

const READ_PER_MIN = num('READ_PER_MIN', 240); // per subject (thandi, sipho, agent)
const WRITE_PER_MIN = num('WRITE_PER_MIN', 3); // submit+withdraw pairs per customer
const DURATION = __ENV.DURATION || '3m';
const READ_P95_MS = num('READ_P95_MS', 300); // SLO; only lower/raise for the negative control
const WRITE_P95_MS = num('WRITE_P95_MS', 500);

const PER_REPLICA_OVERALL = 300;
const PER_REPLICA_SUBMITS = 10;
const SETUP_SUBMITS = 3;
const SETUP_REQUESTS = 40;
if (READ_PER_MIN + 2 * WRITE_PER_MIN + SETUP_REQUESTS >= PER_REPLICA_OVERALL || WRITE_PER_MIN + SETUP_SUBMITS >= PER_REPLICA_SUBMITS) {
  throw new Error('rate plan would risk the per-replica rate limits; lower READ_PER_MIN / WRITE_PER_MIN');
}

const USERS = {
  thandi: { password: 'Thandi-Demo-2026!', role: 'customer' },
  sipho: { password: 'Sipho-Demo-2026!', role: 'customer' },
  agent: { password: 'Agent-Demo-2026!', role: 'agent' },
};

// ---- metrics ------------------------------------------------------------------------------------------------------
const readDuration = new Trend('read_duration', true); // GET /api/v1/*
const writeDuration = new Trend('write_duration', true); // POST submit + POST withdraw
const readRequests = new Counter('read_requests');
const writeRequests = new Counter('write_requests');
const rateLimited = new Counter('http_429'); // any 429 invalidates the run
const errorRate = new Rate('errors'); // unexpected status on a measured request
const endpoint = {};
for (const name of ['tx_list', 'tx_detail', 'disputes_list', 'dispute_detail', 'agent_queue', 'agent_detail', 'submit', 'withdraw']) {
  endpoint[name] = new Trend('ep_' + name, true);
}

export const options = {
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  scenarios: {
    reads_thandi: readScenario('thandi'),
    reads_sipho: readScenario('sipho'),
    reads_agent: readScenario('agent'),
    writes_thandi: writeScenario('thandi'),
    writes_sipho: writeScenario('sipho'),
  },
  thresholds: {
    read_duration: [`p(95)<${READ_P95_MS}`],
    write_duration: [`p(95)<${WRITE_P95_MS}`],
    errors: ['rate<0.01'],
    http_req_failed: ['rate<0.01'],
    http_429: ['count==0'],
    dropped_iterations: ['count==0'], // the generator really delivered the planned arrival rate
  },
};

function readScenario(who) {
  return {
    executor: 'constant-arrival-rate',
    rate: READ_PER_MIN,
    timeUnit: '1m',
    duration: DURATION,
    preAllocatedVUs: 4,
    maxVUs: 20,
    exec: 'reads',
    env: { WHO: who },
  };
}

function writeScenario(who) {
  return {
    executor: 'constant-arrival-rate',
    rate: WRITE_PER_MIN,
    timeUnit: '1m',
    duration: DURATION,
    preAllocatedVUs: 2,
    maxVUs: 5,
    exec: 'writes',
    env: { WHO: who },
  };
}

// ---- tiny cookie jar --------------------------------------------------------------------------------------------
// k6's built-in jar follows Go's rules and never sends a `Secure` cookie over http, but the BFF marks its cookies
// Secure (CookieSecurePolicy.Always). Browsers treat http://localhost as secure; this script mimics that by hand.
function storeCookies(jar, res) {
  for (const name of Object.keys(res.cookies)) {
    for (const c of res.cookies[name]) {
      if (c.value === '' || (c.max_age !== undefined && c.max_age < 0)) delete jar[c.name];
      else jar[c.name] = c.value;
    }
  }
}

const cookieHeader = (jar) => Object.keys(jar).map((n) => `${n}=${jar[n]}`).join('; ');

// One request to either origin, with the public Host header. Does not follow redirects.
function send(origin, method, path, jar, { headers = {}, body = null, tags = {}, responseCallback } = {}) {
  const isAuth = origin === AUTH_ORIGIN;
  const params = {
    redirects: 0,
    headers: Object.assign({ Host: (isAuth ? PUBLIC_AUTH : PUBLIC_APP).replace(/^https?:\/\//, ''), Cookie: cookieHeader(jar) }, headers),
    tags,
  };
  if (responseCallback) params.responseCallback = responseCallback;
  const res = http.request(method, origin + path, body, params);
  storeCookies(jar, res);
  return res;
}

// Map an absolute public URL (or a relative one) back to {origin, path}.
function locate(location, currentOrigin) {
  if (location.startsWith(PUBLIC_AUTH)) return { origin: AUTH_ORIGIN, path: location.slice(PUBLIC_AUTH.length) || '/' };
  if (location.startsWith(PUBLIC_APP)) return { origin: APP_ORIGIN, path: location.slice(PUBLIC_APP.length) || '/' };
  if (location.startsWith('/')) return { origin: currentOrigin, path: location };
  fail(`unexpected redirect target ${location}`);
}

// ---- real sign-in: /bff/login -> Keycloak form -> /signin-oidc -> session cookie ---------------------------------
function signIn(user) {
  const jars = { [APP_ORIGIN]: {}, [AUTH_ORIGIN]: {} };
  let step = { origin: APP_ORIGIN, path: '/bff/login?returnUrl=%2F', method: 'GET', body: null };
  for (let hop = 0; hop < 12; hop++) {
    const jar = jars[step.origin];
    const headers = step.body ? { 'Content-Type': 'application/x-www-form-urlencoded' } : {};
    const res = send(step.origin, step.method, step.path, jar, { headers, body: step.body, tags: { name: 'login' } });
    if (res.status >= 300 && res.status < 400) {
      step = { ...locate(res.headers.Location, step.origin), method: 'GET', body: null };
      continue;
    }
    if (res.status === 200 && step.origin === AUTH_ORIGIN) {
      const form = parseHTML(res.body).find('form#kc-form-login');
      if (form.size() === 0) fail(`${user}: Keycloak did not return the login form (status ${res.status})`);
      const action = form.attr('action');
      step = {
        ...locate(action, AUTH_ORIGIN),
        method: 'POST',
        body: { username: user, password: USERS[user].password, credentialId: '' },
      };
      continue;
    }
    if (res.status === 200 && step.origin === APP_ORIGIN) {
      const session = jars[APP_ORIGIN];
      if (!session.dp_session) fail(`${user}: signed in but no dp_session cookie (cookies: ${Object.keys(session)})`);
      return session;
    }
    fail(`${user}: unexpected ${res.status} at ${step.origin}${step.path}`);
  }
  return fail(`${user}: too many redirects during sign-in`);
}

// ---- API helper for setup and for the scenarios -----------------------------------------------------------------
function api(session, method, path, { body = null, headers = {}, name } = {}) {
  const h = Object.assign({ 'X-CSRF': '1', Accept: 'application/json' }, headers);
  if (body !== null) h['Content-Type'] = 'application/json';
  return send(APP_ORIGIN, method, path, session, { headers: h, body: body === null ? null : JSON.stringify(body), tags: { name: name || path } });
}

function json(res, what) {
  if (res.status !== 200 && res.status !== 201) fail(`${what}: status ${res.status} ${res.body}`);
  return res.json();
}

function uuidv4() {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
  });
}

function pick(list) {
  return list[Math.floor(Math.random() * list.length)];
}

export function setup() {
  const data = { users: {} };
  for (const user of Object.keys(USERS)) {
    const session = signIn(user);
    const me = json(api(session, 'GET', '/bff/user'), `${user} /bff/user`);
    check(me, { [`${user} has the expected role`]: (m) => JSON.stringify(m).includes(USERS[user].role === 'agent' ? 'dispute-agent' : 'customer') });
    data.users[user] = { cookie: cookieHeader(session) };
  }

  for (const user of ['thandi', 'sipho']) {
    const session = cookiesOf(data.users[user]);
    // Walk every page: ids for detail reads, cursors for paged reads, and the disputable pool for the write loop.
    const txIds = [];
    const cursors = [''];
    const disputable = [];
    let cursor = '';
    do {
      const q = `/api/v1/transactions?limit=25${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`;
      const page = json(api(session, 'GET', q), `${user} transactions`);
      for (const t of page.items) {
        txIds.push(t.id);
        if (t.isDisputable) disputable.push(t.id);
      }
      cursor = page.nextCursor || '';
      if (cursor) cursors.push(cursor);
    } while (cursor);

    // Make sure there are a few disputes to read (history, detail, agent queue); they stay open on purpose.
    const reasons = json(api(session, 'GET', '/api/v1/dispute-reasons'), `${user} reasons`);
    let disputes = json(api(session, 'GET', '/api/v1/disputes?limit=100'), `${user} disputes`).items;
    const blocked = new Set(disputes.map((d) => d.transactionId));
    const spare = disputable.filter((id) => !blocked.has(id));
    for (let i = 0; disputes.length < SETUP_SUBMITS && i < spare.length; i++) {
      const created = api(session, 'POST', '/api/v1/disputes', {
        body: { id: uuidv4(), transactionId: spare[i], reasonCode: reasons[0].code, description: 'k6 setup: seed dispute for read traffic.' },
        name: 'setup submit',
      });
      json(created, `${user} setup submit`);
      blocked.add(spare[i]);
      disputes = json(api(session, 'GET', '/api/v1/disputes?limit=100'), `${user} disputes`).items;
    }
    Object.assign(data.users[user], {
      txIds,
      cursors,
      disputeIds: disputes.map((d) => d.id),
      writePool: disputable.filter((id) => !blocked.has(id)),
      reasonCode: reasons[0].code,
    });
  }

  const agent = cookiesOf(data.users.agent);
  const queue = json(api(agent, 'GET', '/api/v1/agent/disputes?limit=100'), 'agent queue');
  if (queue.items.length === 0) fail('agent queue is empty after seeding disputes');
  data.users.agent.queueIds = queue.items.map((d) => d.id);
  data.users.agent.cursors = [''];
  if (queue.nextCursor) data.users.agent.cursors.push(queue.nextCursor);
  return data;
}

function cookiesOf(user) {
  const jar = {};
  for (const pair of user.cookie.split('; ')) {
    const i = pair.indexOf('=');
    jar[pair.slice(0, i)] = pair.slice(i + 1);
  }
  return jar;
}

// Per-VU cache so the cookie string is parsed once.
const sessions = {};
function sessionFor(data, who) {
  if (!sessions[who]) sessions[who] = cookiesOf(data.users[who]);
  return sessions[who];
}

// ---- measured calls ---------------------------------------------------------------------------------------------
function measured(kind, name, session, method, path, okStatuses, opts) {
  const res = api(session, method, path, { ...opts, name });
  const group = kind === 'read' ? readDuration : writeDuration;
  group.add(res.timings.duration);
  endpoint[name].add(res.timings.duration);
  (kind === 'read' ? readRequests : writeRequests).add(1);
  if (res.status === 429) rateLimited.add(1);
  const ok = okStatuses.includes(res.status);
  errorRate.add(!ok);
  if (!ok) console.error(`${name} ${method} ${path} -> ${res.status} ${String(res.body).slice(0, 200)}`);
  return res;
}

// One request per iteration, so the arrival rate IS the request rate.
export function reads(data) {
  const who = __ENV.WHO;
  const u = data.users[who];
  const session = sessionFor(data, who);
  const roll = Math.random();
  if (who === 'agent') {
    if (roll < 0.5) {
      const c = pick(u.cursors);
      measured('read', 'agent_queue', session, 'GET', `/api/v1/agent/disputes?limit=25${c ? `&cursor=${encodeURIComponent(c)}` : ''}`, [200]);
    } else {
      measured('read', 'agent_detail', session, 'GET', `/api/v1/agent/disputes/${pick(u.queueIds)}`, [200]);
    }
    return;
  }
  if (roll < 0.45) {
    const c = pick(u.cursors);
    measured('read', 'tx_list', session, 'GET', `/api/v1/transactions?limit=25${c ? `&cursor=${encodeURIComponent(c)}` : ''}`, [200]);
  } else if (roll < 0.75) {
    measured('read', 'tx_detail', session, 'GET', `/api/v1/transactions/${pick(u.txIds)}`, [200]);
  } else if (roll < 0.88) {
    measured('read', 'disputes_list', session, 'GET', '/api/v1/disputes?limit=25', [200]);
  } else {
    measured('read', 'dispute_detail', session, 'GET', `/api/v1/disputes/${pick(u.disputeIds)}`, [200]);
  }
}

// Submit with a client UUID, then withdraw with the ETag it returned.
export function writes(data) {
  const who = __ENV.WHO;
  const u = data.users[who];
  const session = sessionFor(data, who);
  const created = measured('write', 'submit', session, 'POST', '/api/v1/disputes', [201], {
    body: {
      id: uuidv4(),
      transactionId: pick(u.writePool),
      reasonCode: u.reasonCode,
      description: 'k6 smoke: the merchant charged me twice for one purchase.',
    },
  });
  if (created.status !== 201) return;
  const id = created.json().id;
  const etag = created.headers.Etag;
  const withdrawn = measured('write', 'withdraw', session, 'POST', `/api/v1/disputes/${id}/withdraw`, [200], { headers: { 'If-Match': etag } });
  check(withdrawn, { 'withdrawn dispute is Withdrawn': (r) => r.status === 200 && r.json().status === 'Withdrawn' });
}
