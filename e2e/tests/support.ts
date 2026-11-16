import { expect, type Page } from '@playwright/test';

export const DEMO = {
  customer: { user: 'thandi', password: 'Thandi-Demo-2026!', name: 'Thandi Nkosi' },
  customer2: { user: 'sipho', password: 'Sipho-Demo-2026!', name: 'Sipho Dlamini' },
  agent: { user: 'agent', password: 'Agent-Demo-2026!', name: 'Anele Agent' },
  agent2: { user: 'agent2', password: 'Agent2-Demo-2026!', name: 'Lindiwe Agent' },
  agent3: { user: 'agent3', password: 'Agent3-Demo-2026!', name: 'Themba Agent' },
};

/** Collect Content-Security-Policy violations in the page; assert none at the end of a test. */
export async function trackCspViolations(page: Page): Promise<() => Promise<string[]>> {
  await page.addInitScript(() => {
    (window as unknown as { __csp: string[] }).__csp = [];
    document.addEventListener('securitypolicyviolation', (e) => {
      (window as unknown as { __csp: string[] }).__csp.push(`${e.violatedDirective} ${e.blockedURI}`);
    });
  });
  return async () => page.evaluate(() => (window as unknown as { __csp?: string[] }).__csp ?? []);
}

export async function signIn(page: Page, who: { user: string; password: string; name: string }) {
  await page.goto('/');
  await page.getByRole('button', { name: /sign in/i }).click();
  await page.locator('#username').fill(who.user);
  await page.locator('#password').fill(who.password);
  await page.locator('#kc-login').click();
  await expect(page.getByText(who.name).first()).toBeVisible();
}

export async function signOut(page: Page) {
  await page.getByRole('button', { name: /log ?out/i }).click();
}

export interface Api {
  status: number;
  etag: string | null;
  type: string | null;
  body: any; // eslint-disable-line @typescript-eslint/no-explicit-any
}

/** A same-origin API call with the session cookie and the CSRF header, like the SPA makes. */
export function api(page: Page, method: string, path: string, opts: { headers?: Record<string, string>; json?: unknown } = {}) {
  return page.evaluate(
    async ({ method, path, opts }): Promise<Api> => {
      const r = await fetch(path, {
        method,
        headers: {
          'X-CSRF': '1',
          ...(opts.json === undefined ? {} : { 'Content-Type': 'application/json' }),
          ...opts.headers,
        },
        ...(opts.json === undefined ? {} : { body: JSON.stringify(opts.json) }),
      });
      return {
        status: r.status,
        etag: r.headers.get('etag'),
        type: r.headers.get('content-type'),
        body: await r.json().catch(() => null),
      };
    },
    { method, path, opts },
  );
}

/**
 * A transaction nobody has disputed in this run (or any earlier run on this stack): pick at random from
 * every disputable one, so reruns do not pile onto the same row and hit the 3-per-transaction cap.
 */
export async function pickDisputable(page: Page): Promise<{ id: string; merchantName: string }> {
  const found: { id: string; merchantName: string }[] = [];
  let cursor: string | null = null;
  do {
    const r: Api = await api(page, 'GET', `/api/v1/transactions?limit=100${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`);
    expect(r.status).toBe(200);
    for (const t of r.body.items) if (t.isDisputable) found.push({ id: t.id, merchantName: t.merchantName });
    cursor = r.body.nextCursor;
  } while (cursor);
  expect(found.length).toBeGreaterThan(0);
  return found[Math.floor(Math.random() * found.length)]!;
}

export const GUID = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}';
export const DESCRIPTION = 'The merchant charged me twice for a single purchase.';

export async function fillValid(page: Page, description = DESCRIPTION) {
  await page.getByLabel(/^Reason/).selectOption({ index: 1 });
  await page.getByLabel(/^What happened\?/).fill(description);
}

/** Submit through the API (the same call the form makes); for tests whose subject is not the form. */
export async function submitViaApi(page: Page, transactionId: string): Promise<string> {
  const reason = (await api(page, 'GET', '/api/v1/dispute-reasons')).body[0].code as string;
  const id = crypto.randomUUID();
  // Submits are limited to 10 a minute per customer (a fixed window). The same id is a safe replay, so a refused one is
  // retried until the window turns over, instead of failing a test that is not about the limit.
  let created = await api(page, 'POST', '/api/v1/disputes', {
    json: { id, transactionId, reasonCode: reason, description: DESCRIPTION },
  });
  for (let attempt = 0; created.status === 429 && attempt < 14; attempt++) {
    await page.waitForTimeout(5_000);
    created = await api(page, 'POST', '/api/v1/disputes', {
      json: { id, transactionId, reasonCode: reason, description: DESCRIPTION },
    });
  }
  expect(created.status).toBe(201);
  return id;
}

/** Leave the stack as found: a dispute the test left open is withdrawn so it does not clutter the queue. */
export async function withdrawIfOpen(customer: Page, id: string) {
  if (!id) return; // the test failed before a dispute existed
  const current = await api(customer, 'GET', `/api/v1/disputes/${id}`).catch(() => null);
  if (current?.status !== 200 || !['Submitted', 'UnderReview'].includes(current.body.status)) return;
  await api(customer, 'POST', `/api/v1/disputes/${id}/withdraw`, {
    headers: { 'If-Match': current.etag! },
  }).catch(() => null);
}
