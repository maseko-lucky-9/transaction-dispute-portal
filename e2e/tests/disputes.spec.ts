import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { DEMO, signIn, trackCspViolations } from './support';

interface Api {
  status: number;
  etag: string | null;
  type: string | null;
  body: any; // eslint-disable-line @typescript-eslint/no-explicit-any
}

/** A same-origin API call with the session cookie and the CSRF header, like the SPA makes. */
function api(page: Page, method: string, path: string, opts: { headers?: Record<string, string>; json?: unknown } = {}) {
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
async function pickDisputable(page: Page): Promise<{ id: string; merchantName: string }> {
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

const GUID = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}';
const DESCRIPTION = 'The merchant charged me twice for a single purchase.';

async function fillValid(page: Page, description = DESCRIPTION) {
  await page.getByLabel(/^Reason/).selectOption({ index: 1 });
  await page.getByLabel(/^What happened\?/).fill(description);
}

test('a customer disputes a transaction, sees it in history, replays it safely, withdraws it and disputes again', async ({
  page,
}) => {
  const violations = await trackCspViolations(page);
  await signIn(page, DEMO.customer);
  const tx = await pickDisputable(page);

  let posts = 0;
  page.on('request', (r) => {
    if (r.method() === 'POST' && new URL(r.url()).pathname === '/api/v1/disputes') posts++;
  });

  await test.step('an invalid form shows inline errors and sends no request', async () => {
    await page.goto(`/transactions/${tx.id}/dispute`);
    await expect(page.getByRole('form', { name: 'Dispute this transaction' })).toBeVisible();

    await page.getByRole('button', { name: 'Submit dispute' }).click();
    await expect(page.getByLabel(/^Reason/)).toHaveAttribute('aria-invalid', 'true');
    await expect(page.getByLabel(/^What happened\?/)).toHaveAttribute('aria-invalid', 'true');
    await expect(page.getByLabel(/^Reason/)).toBeFocused(); // focus moves to the first invalid field

    // Card/ID-looking numbers are refused in the browser, with the same rule the API applies.
    await fillValid(page, 'Charged twice, my card 4111111111111111 was used');
    await page.getByRole('button', { name: 'Submit dispute' }).click();
    await expect(page.getByText('Please remove card or ID numbers')).toBeVisible();

    // A bad amount too: more than the transaction, and too many decimals.
    await page.getByLabel(/^What happened\?/).fill(DESCRIPTION);
    await page.getByLabel(/^Amount to dispute/).fill('1.234');
    await page.getByRole('button', { name: 'Submit dispute' }).click();
    await expect(page.getByLabel(/^Amount to dispute/)).toHaveAttribute('aria-invalid', 'true');
    expect(posts).toBe(0);

    expect(await violations()).toEqual([]);
    const axe = await new AxeBuilder({ page }).analyze();
    expect(axe.violations).toEqual([]);
  });

  let disputeId = '';
  await test.step('a valid form submits once and opens the dispute', async () => {
    await page.getByLabel(/^Amount to dispute/).fill('');
    await page.getByRole('button', { name: 'Submit dispute' }).click();
    await expect(page).toHaveURL(new RegExp(`/disputes/${GUID}$`));
    disputeId = page.url().split('/').pop()!;
    expect(posts).toBe(1);

    await expect(page.getByRole('status').filter({ hasText: 'Your dispute was submitted' })).toBeVisible();
    await expect(page.getByRole('heading', { level: 1 })).toContainText(tx.merchantName);
    await expect(page.getByText('Submitted', { exact: true }).first()).toBeVisible();
    await expect(page.getByText(DESCRIPTION)).toBeVisible();
    // The timeline names who acted in words, never an id.
    await expect(page.getByText('You').first()).toBeVisible();

    expect(await violations()).toEqual([]);
    const axe = await new AxeBuilder({ page }).analyze();
    expect(axe.violations).toEqual([]);
  });

  await test.step('it is in the history', async () => {
    await page.getByRole('link', { name: 'My disputes' }).first().click();
    await expect(page.getByRole('heading', { name: 'My disputes' })).toBeVisible();
    const row = page.getByRole('region', { name: 'Disputes table' }).locator('tbody tr').filter({ hasText: tx.merchantName });
    await expect(row.first()).toBeVisible();
    await expect(row.first().getByText('Submitted')).toBeVisible();
  });

  await test.step('the same id is an idempotent replay; a second dispute for the transaction is a 409', async () => {
    const original = await api(page, 'GET', `/api/v1/disputes/${disputeId}`);
    expect(original.status).toBe(200);
    expect(original.etag).toMatch(/^"\d+"$/);

    const body = (id: string) => ({
      id,
      transactionId: tx.id,
      reasonCode: original.body.reasonCode,
      description: DESCRIPTION,
    });
    const replay = await api(page, 'POST', '/api/v1/disputes', { json: body(disputeId) });
    expect(replay.status).toBe(200);
    expect(replay.body.id).toBe(disputeId);

    const other = await api(page, 'POST', '/api/v1/disputes', { json: body(crypto.randomUUID()) });
    expect(other.status).toBe(409);
    expect(other.type).toContain('application/problem+json');
    expect(other.body.code).toBe('dispute_exists');
    expect(other.body.existingDisputeId).toBe(disputeId);

    // The server applies the same sensitive-number rule as the form (the API is the authority).
    const sensitive = await api(page, 'POST', '/api/v1/disputes', {
      json: { ...body(crypto.randomUUID()), description: 'my card 4111111111111111 was used' },
    });
    expect(sensitive.status).toBe(400);
    expect(sensitive.body.code).toBe('sensitive_data');
  });

  await test.step('the transaction now says it already has a dispute', async () => {
    await page.goto('/transactions');
    await expect(page.getByRole('region', { name: 'Transactions table' })).toBeVisible();
    const check = await api(page, 'GET', `/api/v1/transactions/${tx.id}`);
    expect(check.body.isDisputable).toBe(false);
    expect(check.body.dispute.id).toBe(disputeId);
    expect(check.body.disputeBlockReasons.length).toBeGreaterThan(0);
  });

  await test.step('withdraw needs the current version: none is 428, a stale one is 412 and names the current', async () => {
    const none = await api(page, 'POST', `/api/v1/disputes/${disputeId}/withdraw`);
    expect(none.status).toBe(428);
    const stale = await api(page, 'POST', `/api/v1/disputes/${disputeId}/withdraw`, { headers: { 'If-Match': '"1"' } });
    expect(stale.status).toBe(412);
    expect(stale.etag).toMatch(/^"\d+"$/); // the current version, so a client can reload
    expect(stale.body.code).toBeTruthy();
  });

  await test.step('withdraw from the page', async () => {
    await page.goto(`/disputes/${disputeId}`);
    await page.getByRole('button', { name: 'Withdraw dispute' }).click();
    await expect(page.getByText('Withdraw this dispute?')).toBeFocused();
    expect(await violations()).toEqual([]);
    await page.getByRole('button', { name: 'Yes, withdraw' }).click();
    await expect(page.getByText('Dispute withdrawn.')).toBeVisible();
    await expect(page.getByText('Withdrawn', { exact: true }).first()).toBeVisible();
    await expect(page.getByRole('button', { name: 'Withdraw dispute' })).toHaveCount(0);

    // Withdrawing again is a state conflict, not an error page.
    const again = await api(page, 'POST', `/api/v1/disputes/${disputeId}/withdraw`, {
      headers: { 'If-Match': (await api(page, 'GET', `/api/v1/disputes/${disputeId}`)).etag! },
    });
    expect(again.status).toBe(409);
    expect(again.body.code).toBe('invalid_transition');
  });

  await test.step('after a withdrawal the transaction can be disputed again', async () => {
    await page.goto(`/transactions/${tx.id}/dispute`);
    await fillValid(page, 'I am raising this again with more detail: the second charge has no matching receipt.');
    await page.getByRole('button', { name: 'Submit dispute' }).click();
    await expect(page).toHaveURL(new RegExp(`/disputes/${GUID}$`));
    const secondId = page.url().split('/').pop()!;
    expect(secondId).not.toBe(disputeId);
    // Leave the stack as found: this one is withdrawn too.
    const etag = (await api(page, 'GET', `/api/v1/disputes/${secondId}`)).etag!;
    expect((await api(page, 'POST', `/api/v1/disputes/${secondId}/withdraw`, { headers: { 'If-Match': etag } })).status).toBe(200);
  });

  expect(await violations()).toEqual([]);
});

test('the Dispute link in the transactions table opens the form for that row', async ({ page }) => {
  await signIn(page, DEMO.customer);
  await page.getByRole('link', { name: /my transactions/i }).click();
  const link = page
    .getByRole('region', { name: 'Transactions table' })
    .getByRole('link', { name: /^Dispute / })
    .first();
  await expect(link).toBeVisible();
  const label = (await link.getAttribute('aria-label'))!;
  await link.click();
  await expect(page).toHaveURL(new RegExp(`/transactions/${GUID}/dispute$`));
  await expect(page.getByRole('form', { name: 'Dispute this transaction' })).toBeVisible();
  // The form says which transaction it is for.
  await expect(page.getByText(label.replace(/^Dispute /, '').split(',')[0]!).first()).toBeVisible();
});

test("a customer can never see, replay or withdraw another customer's dispute, and an agent is refused", async ({
  browser,
}) => {
  const thandi = await browser.newContext();
  const sipho = await browser.newContext();
  const agent = await browser.newContext();
  const [th, sp, ag] = await Promise.all([thandi.newPage(), sipho.newPage(), agent.newPage()]);
  await signIn(th, DEMO.customer);
  await signIn(sp, DEMO.customer2);
  await signIn(ag, DEMO.agent);

  const tx = await pickDisputable(th);
  const id = crypto.randomUUID();
  const created = await api(th, 'POST', '/api/v1/disputes', {
    json: { id, transactionId: tx.id, reasonCode: (await api(th, 'GET', '/api/v1/dispute-reasons')).body[0].code, description: DESCRIPTION },
  });
  expect(created.status).toBe(201);

  // Sipho: the id is a 404 (never 403), for read and for withdraw; the list does not contain it.
  const read = await api(sp, 'GET', `/api/v1/disputes/${id}`);
  expect(read.status).toBe(404);
  expect(read.type).toContain('application/problem+json');
  expect(JSON.stringify(read.body)).not.toContain(tx.merchantName);
  expect((await api(sp, 'POST', `/api/v1/disputes/${id}/withdraw`, { headers: { 'If-Match': created.etag! } })).status).toBe(404);
  const siphosList = await api(sp, 'GET', '/api/v1/disputes?limit=100');
  expect(siphosList.body.items.map((d: { id: string }) => d.id)).not.toContain(id);

  // Nor can he dispute Thandi's transaction (404: the transaction is not his).
  const hijack = await api(sp, 'POST', '/api/v1/disputes', {
    json: { id: crypto.randomUUID(), transactionId: tx.id, reasonCode: created.body.reasonCode, description: DESCRIPTION },
  });
  expect(hijack.status).toBe(404);

  // The agent role has no customer routes (the back-office has its own).
  expect((await api(ag, 'GET', '/api/v1/disputes')).status).toBe(403);
  expect((await api(ag, 'GET', `/api/v1/disputes/${id}`)).status).toBe(403);

  // Clean up: Thandi withdraws.
  expect((await api(th, 'POST', `/api/v1/disputes/${id}/withdraw`, { headers: { 'If-Match': created.etag! } })).status).toBe(200);
  await Promise.all([thandi.close(), sipho.close(), agent.close()]);
});

test('malformed dispute requests are 400/404 problem documents, never 500', async ({ page }) => {
  // The second customer: POST /disputes is rate limited per user (10/min), and the tests above spend Thandi's.
  await signIn(page, DEMO.customer2);
  const tx = await pickDisputable(page);
  const reason = (await api(page, 'GET', '/api/v1/dispute-reasons')).body[0].code as string;
  const ok = { id: crypto.randomUUID(), transactionId: tx.id, reasonCode: reason, description: DESCRIPTION };

  const cases: [string, unknown, number][] = [
    ['empty id', { ...ok, id: '00000000-0000-0000-0000-000000000000' }, 400],
    ['unknown reason', { ...ok, reasonCode: 'nope' }, 400],
    ['short description', { ...ok, description: 'too short' }, 400],
    ['long description', { ...ok, description: 'x'.repeat(2001) }, 400],
    ['bad amount', { ...ok, amount: '1.234' }, 400],
    ['unknown transaction', { ...ok, transactionId: crypto.randomUUID() }, 404],
  ];
  for (const [name, json, status] of cases) {
    const r = await api(page, 'POST', '/api/v1/disputes', { json });
    expect(r.status, name).toBe(status);
    expect(r.type, name).toContain('application/problem+json');
    expect(r.body.traceId, name).toBeTruthy();
  }
  for (const qs of ['status=Nope', 'limit=0', 'limit=101', 'limit=abc']) {
    const r = await api(page, 'GET', `/api/v1/disputes?${qs}`);
    expect(r.status, qs).toBe(400);
    expect(r.type, qs).toContain('application/problem+json');
  }
  expect((await api(page, 'GET', '/api/v1/disputes/not-a-guid')).status).toBe(404);
});
