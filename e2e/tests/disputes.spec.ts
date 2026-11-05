import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Locator, type Page } from '@playwright/test';
import { api, DEMO, DESCRIPTION, fillValid, GUID, pickDisputable, signIn, trackCspViolations } from './support';

/** CSP first: axe evaluates strings in the page, which the CSP blocks by design, so read violations before it runs. */
async function expectNoCspOrAxeViolations(page: Page, violations: () => Promise<string[]>) {
  expect(await violations()).toEqual([]);
  const axe = await new AxeBuilder({ page }).analyze();
  expect(axe.violations).toEqual([]);
}

/** A field in error is announced and wired: aria-invalid, and an aria-describedby that resolves to the message. */
async function expectFieldError(page: Page, field: Locator, errorId: string, message: RegExp) {
  await expect(field).toHaveAttribute('aria-invalid', 'true');
  await expect(field).toHaveAttribute('aria-describedby', new RegExp(`(^| )${errorId}( |$)`));
  await expect(page.locator(`#${errorId}`)).toHaveText(message);
  await expect(page.locator(`#${errorId}`)).toHaveAttribute('role', 'alert');
  await expect(field).toHaveAccessibleDescription(message); // the browser's own resolution of aria-describedby
}

async function expectNoFieldError(page: Page, field: Locator, errorId: string) {
  await expect(field).not.toHaveAttribute('aria-invalid', 'true');
  await expect(page.locator(`#${errorId}`)).toHaveCount(0);
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
    await expect(page.getByText('You submitted this dispute')).toBeVisible();

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

test('an empty or invalid dispute form shows inline errors per field and sends no request', async ({ page }) => {
  const violations = await trackCspViolations(page);
  await signIn(page, DEMO.customer);
  // Independent of the other tests, and nothing is submitted, so the picked transaction stays disputable.
  const tx = await pickDisputable(page);
  const detail = await api(page, 'GET', `/api/v1/transactions/${tx.id}`);
  expect(detail.status).toBe(200);
  const maximum = detail.body.amount as string;
  const tooMuch = (Number(maximum) + 1).toFixed(2); // more than the transaction's own amount

  // The listener counts every attempt. The route also refuses it, so a regression in the form cannot
  // create a real dispute (and cannot spend the 10-a-minute submit limit or the transaction).
  const sent: string[] = [];
  const refused: string[] = [];
  const isSubmit = (method: string, url: string) => method === 'POST' && new URL(url).pathname === '/api/v1/disputes';
  page.on('request', (r) => {
    if (isSubmit(r.method(), r.url())) sent.push(r.url());
  });
  await page.route('**/api/v1/disputes', (route) => {
    if (!isSubmit(route.request().method(), route.request().url())) return route.fallback();
    refused.push(route.request().url());
    return route.abort();
  });
  const expectNothingSent = () => {
    expect(sent, 'POST /api/v1/disputes attempts').toEqual([]);
    expect(refused, 'POST /api/v1/disputes refused by the route').toEqual([]);
  };

  const reason = page.getByLabel(/^Reason/);
  const description = page.getByLabel(/^What happened\?/);
  const amount = page.getByLabel(/^Amount to dispute/);
  const submit = page.getByRole('button', { name: 'Submit dispute' });

  await test.step('the form opens for the picked transaction', async () => {
    await page.goto(`/transactions/${tx.id}/dispute`);
    await expect(page.getByRole('form', { name: 'Dispute this transaction' })).toBeVisible();
    await expect(page.getByText(tx.merchantName).first()).toBeVisible();
  });

  await test.step('submitting it empty flags each required field and focuses the first', async () => {
    await submit.click();
    await expectFieldError(page, reason, 'dispute-reason-error', /Choose a reason/);
    await expectFieldError(page, description, 'dispute-description-error', /Describe what went wrong, in at least 10 characters/);
    await expectNoFieldError(page, amount, 'dispute-amount-error'); // the amount is optional: empty means all of it
    await expect(reason).toBeFocused();
    expectNothingSent();
    await expectNoCspOrAxeViolations(page, violations);
  });

  await test.step('a description that is too short once trimmed is refused', async () => {
    await reason.selectOption({ index: 1 });
    await expectNoFieldError(page, reason, 'dispute-reason-error');
    await description.fill('  too short  '); // 13 characters typed, 9 counted: the length is on the trimmed text
    await submit.click();
    await expectFieldError(page, description, 'dispute-description-error', /Describe what went wrong, in at least 10 characters/);
    await expect(description).toBeFocused();
    expectNothingSent();
  });

  await test.step('an amount above the transaction amount is refused', async () => {
    await description.fill(DESCRIPTION);
    await expectNoFieldError(page, description, 'dispute-description-error');
    await amount.fill(tooMuch);
    await submit.click();
    await expectFieldError(page, amount, 'dispute-amount-error', /The amount cannot be more than/);
    await expect(amount).toBeFocused();
    expectNothingSent();
  });

  await test.step('a Luhn-valid 16-digit run in the description is refused with the sensitive-data message', async () => {
    await description.fill('Charged twice, my card 4111111111111111 was used');
    await submit.click();
    await expectFieldError(page, description, 'dispute-description-error', /Please remove card or ID numbers/);
    await expectFieldError(page, amount, 'dispute-amount-error', /The amount cannot be more than/); // still wrong
    await expect(description).toBeFocused(); // the first invalid field in form order, not the last one touched
    expectNothingSent();
    await expectNoCspOrAxeViolations(page, violations); // the form with two fields in error
  });

  await test.step('fixing each problem clears its error, still without a request', async () => {
    // Only the checksum tells a card number from any other long number: this 16-digit run fails Luhn.
    await description.fill('Charged twice, reference 4111111111111112 on the slip');
    await expectNoFieldError(page, description, 'dispute-description-error');
    // The transaction's own amount is the most that can be disputed.
    await amount.fill(maximum);
    await expectNoFieldError(page, amount, 'dispute-amount-error');
    expectNothingSent();
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

  // POST /disputes is limited to 10/min per user, so this list stays short; the rest is in the integration tests.
  const cases: [string, unknown, number, string?][] = [
    ['unknown reason', { ...ok, reasonCode: 'nope' }, 400],
    ['short description', { ...ok, description: 'too short' }, 400],
    ['bad amount', { ...ok, amount: '1.234' }, 400],
    ['unknown transaction', { ...ok, transactionId: crypto.randomUUID() }, 404],
    // Postgres refuses 0x00 in text: it must be a 400 here, not a 500 from the insert.
    ['NUL in description', { ...ok, description: 'abcdefghij\u0000k' }, 400],
    ['NUL in reasonCode', { ...ok, reasonCode: 'x\u0000' }, 400],
    // The same card number, written the ways people paste it, is still a card number.
    // The scan limit is on the raw text, the stored text is trimmed: padding must not slip a number past the scan.
    ['card, padded past the scan limit', { ...ok, description: ' '.repeat(10_001) + 'paid with 4111111111111111 twice' }, 400, 'sensitive_data'],
    ['card, double spaces', { ...ok, description: 'paid with 4111  1111  1111  1111 twice' }, 400, 'sensitive_data'],
  ];
  const wrong: string[] = [];
  for (const [name, json, status, code] of cases) {
    const r = await api(page, 'POST', '/api/v1/disputes', { json });
    const problem = r.type?.includes('application/problem+json') && r.body?.traceId;
    if (r.status !== status || (code && r.body?.code !== code) || !problem)
      wrong.push(`${name}: got ${r.status} ${r.body?.code ?? ''}, want ${status} ${code ?? ''}`.trim());
  }
  expect(wrong).toEqual([]);
  for (const qs of ['status=Nope', 'limit=0', 'limit=101', 'limit=abc']) {
    const r = await api(page, 'GET', `/api/v1/disputes?${qs}`);
    expect(r.status, qs).toBe(400);
    expect(r.type, qs).toContain('application/problem+json');
  }
  expect((await api(page, 'GET', '/api/v1/disputes/not-a-guid')).status).toBe(404);
});
