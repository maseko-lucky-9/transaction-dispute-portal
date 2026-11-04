import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { DEMO, signIn, trackCspViolations } from './support';

const rows = (page: Page) => page.getByRole('region', { name: 'Transactions table' }).locator('tbody tr');

async function loadEverything(page: Page) {
  // "Load more" until the keyset cursor runs out.
  for (let i = 0; i < 20; i++) {
    const more = page.getByRole('button', { name: 'Load more' });
    if (!(await more.isVisible().catch(() => false))) break;
    const before = await rows(page).count();
    await more.click();
    await expect.poll(async () => rows(page).count()).toBeGreaterThan(before);
  }
}

test('customer pages through their own transactions without duplicates, with a11y and CSP clean', async ({ page }) => {
  const violations = await trackCspViolations(page);
  await signIn(page, DEMO.customer);
  await page.getByRole('link', { name: /my transactions/i }).click();
  await expect(page.getByRole('heading', { name: 'Transactions' })).toBeVisible();

  await expect(rows(page).first()).toBeVisible();
  expect(await rows(page).count()).toBe(25); // default page size
  await loadEverything(page);

  const total = await rows(page).count();
  expect(total).toBeGreaterThanOrEqual(50);
  const refs = await rows(page).evaluateAll((trs) => trs.map((tr) => (tr.textContent ?? '').trim()));
  expect(new Set(refs).size).toBe(refs.length); // no duplicate rows across pages

  // Some rows are not disputable and say why (pending / credit / older than the window).
  await expect(page.getByText(/120-day dispute window has closed/i).first()).toBeVisible();
  await expect(page.getByText(/only debits can be disputed/i).first()).toBeVisible();

  expect(await violations()).toEqual([]);
  const axe = await new AxeBuilder({ page }).analyze();
  expect(axe.violations).toEqual([]);
});

test('an invalid filter shows inline errors and sends no request', async ({ page }) => {
  await signIn(page, DEMO.customer);
  await page.getByRole('link', { name: /my transactions/i }).click();
  await expect(rows(page).first()).toBeVisible();

  let listRequests = 0;
  page.on('request', (r) => {
    if (r.url().includes('/api/v1/transactions')) listRequests++;
  });
  await page.getByLabel('From', { exact: true }).fill('2026-10-10');
  await page.getByLabel('To', { exact: true }).fill('2026-10-01'); // before From
  await page.getByRole('button', { name: 'Apply filters' }).click();

  await expect(page.getByRole('alert').first()).toBeVisible();
  await expect(page.getByLabel('To', { exact: true })).toHaveAttribute('aria-invalid', 'true');
  expect(listRequests).toBe(0);

  // A range wider than 366 days is refused client-side too.
  await page.getByLabel('From', { exact: true }).fill('2024-01-01');
  await page.getByLabel('To', { exact: true }).fill('2026-01-01');
  await page.getByRole('button', { name: 'Apply filters' }).click();
  await expect(page.getByLabel('To', { exact: true })).toHaveAttribute('aria-invalid', 'true');
  expect(listRequests).toBe(0);
});

test('a status filter narrows the list through the URL; garbage in the URL falls back to defaults', async ({ page }) => {
  await signIn(page, DEMO.customer);
  await page.goto('/transactions?status=Pending');
  await expect(rows(page).first()).toBeVisible();
  for (const text of await rows(page).evaluateAll((trs) => trs.map((t) => t.textContent ?? ''))) {
    expect(text).toContain('Pending');
  }
  await page.goto('/transactions?limit=banana&from=not-a-date&status=Nope');
  await expect(page.getByRole('heading', { name: 'Transactions' })).toBeVisible();
  await expect(rows(page).first()).toBeVisible();
});

test("a customer can never read another customer's transaction (BOLA), and an agent is refused", async ({ browser }) => {
  const sipho = await browser.newContext();
  const thandi = await browser.newContext();
  const agent = await browser.newContext();
  const [sp, th, ag] = await Promise.all([sipho.newPage(), thandi.newPage(), agent.newPage()]);
  await signIn(sp, DEMO.customer2);
  await signIn(th, DEMO.customer);
  await signIn(ag, DEMO.agent);

  const getJson = (p: Page, path: string) =>
    p.evaluate(async (u) => {
      const r = await fetch(u, { headers: { 'X-CSRF': '1' } });
      return { status: r.status, type: r.headers.get('content-type'), body: await r.json() };
    }, path);

  const siphosFirst = (await getJson(sp, '/api/v1/transactions?limit=1')).body.items[0];
  expect(siphosFirst.id).toBeTruthy();

  const own = await getJson(sp, `/api/v1/transactions/${siphosFirst.id}`);
  expect(own.status).toBe(200);

  const cross = await getJson(th, `/api/v1/transactions/${siphosFirst.id}`);
  expect(cross.status).toBe(404); // never 403: the existence of the id is not revealed
  expect(cross.type).toContain('application/problem+json');
  expect(JSON.stringify(cross.body)).not.toContain(siphosFirst.merchantName);

  // Thandi's list never contains Sipho's reference.
  const thandisAll: string[] = [];
  let cursor: string | null = null;
  do {
    const page: { body: { items: { reference: string }[]; nextCursor: string | null } } = await getJson(
      th,
      `/api/v1/transactions?limit=100${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`,
    );
    thandisAll.push(...page.body.items.map((i) => i.reference));
    cursor = page.body.nextCursor;
  } while (cursor);
  expect(thandisAll).not.toContain(siphosFirst.reference);

  // A cursor minted for Sipho is refused for Thandi.
  const first = await getJson(sp, '/api/v1/transactions?limit=5');
  const stolen = await getJson(th, `/api/v1/transactions?limit=5&cursor=${encodeURIComponent(first.body.nextCursor)}`);
  expect(stolen.status).toBe(400);
  expect(stolen.body.code).toBe('invalid_cursor');

  // The agent role has no customer data.
  expect((await getJson(ag, '/api/v1/transactions')).status).toBe(403);
  await Promise.all([sipho.close(), thandi.close(), agent.close()]);
});
