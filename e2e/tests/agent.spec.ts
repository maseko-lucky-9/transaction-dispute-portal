import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page, type Request } from '@playwright/test';
import { api, DEMO, DESCRIPTION, fillValid, GUID, pickDisputable, signIn, trackCspViolations } from './support';

/**
 * The agent journey. Every test submits its own fresh dispute on a freshly picked transaction, so the
 * tests do not depend on each other or on their order. Customers and agents use separate browser contexts
 * (separate sessions), as they would on separate machines.
 */

const TRANSITIONS = /\/api\/v1\/agent\/disputes\/[^/]+\/transitions$/;
const NOTE = 'We found the duplicate charge and refunded it. It will show on your statement within 3 days.';
const NOTE_REQUIRED = 'Enter a note: the customer will see it';

/** The POSTs to the agent transitions endpoint a page makes, so a test can say "no request was sent". */
function trackTransitions(page: Page): Request[] {
  const sent: Request[] = [];
  page.on('request', (r) => {
    if (r.method() === 'POST' && TRANSITIONS.test(new URL(r.url()).pathname)) sent.push(r);
  });
  return sent;
}

/** Axe evaluates strings in the page, which the CSP blocks by design: read CSP violations first. */
async function expectClean(page: Page, violations: () => Promise<string[]>) {
  expect(await violations()).toEqual([]);
  const axe = await new AxeBuilder({ page }).analyze();
  expect(axe.violations).toEqual([]);
}

/** Submit through the dispute form, like a customer does. Returns the new dispute's id. */
async function submitViaForm(page: Page, transactionId: string, description: string): Promise<string> {
  await page.goto(`/transactions/${transactionId}/dispute`);
  await expect(page.getByRole('form', { name: 'Dispute this transaction' })).toBeVisible();
  await fillValid(page, description);
  await page.getByRole('button', { name: 'Submit dispute' }).click();
  await expect(page).toHaveURL(new RegExp(`/disputes/${GUID}$`));
  return page.url().split('/').pop()!;
}

/** Submit through the API (the same call the form makes); for tests whose subject is not the form. */
async function submitViaApi(page: Page, transactionId: string): Promise<string> {
  const reason = (await api(page, 'GET', '/api/v1/dispute-reasons')).body[0].code as string;
  const id = crypto.randomUUID();
  const created = await api(page, 'POST', '/api/v1/disputes', {
    json: { id, transactionId, reasonCode: reason, description: DESCRIPTION },
  });
  expect(created.status).toBe(201);
  return id;
}

/** Leave the stack as found: a dispute the test left open is withdrawn so it does not clutter the queue. */
async function withdrawIfOpen(customer: Page, id: string) {
  if (!id) return; // the test failed before a dispute existed
  const current = await api(customer, 'GET', `/api/v1/disputes/${id}`).catch(() => null);
  if (current?.status !== 200 || !['Submitted', 'UnderReview'].includes(current.body.status)) return;
  await api(customer, 'POST', `/api/v1/disputes/${id}/withdraw`, {
    headers: { 'If-Match': current.etag! },
  }).catch(() => null);
}

/** The queue is oldest first and keyset-paged: the newest dispute may be behind "Load more". */
async function findInQueue(page: Page, id: string) {
  const table = page.getByRole('region', { name: 'Queue table' });
  const rows = table.locator('tbody tr');
  // Not rooted in `table`: a `has` locator is resolved inside each row. The queue shows no id, but the link has it.
  const link = page.locator(`a[href$="/agent/disputes/${id}"]`);
  await expect(rows.first()).toBeVisible();
  for (let i = 0; i < 20 && (await link.count()) === 0; i++) {
    const more = page.getByRole('button', { name: 'Load more' });
    if (!(await more.isVisible().catch(() => false))) break;
    const before = await rows.count();
    await more.click();
    await expect.poll(async () => rows.count()).toBeGreaterThan(before);
  }
  await expect(link).toBeVisible();
  return { link, row: rows.filter({ has: link }) };
}

test('an agent reviews and accepts a customer dispute; the customer sees the outcome but not who decided', async ({
  browser,
}) => {
  const customerContext = await browser.newContext();
  const agentContext = await browser.newContext();
  const customer = await customerContext.newPage();
  const agent = await agentContext.newPage();
  const customerViolations = await trackCspViolations(customer);
  const agentViolations = await trackCspViolations(agent);
  const description = `${DESCRIPTION} Reference ${crypto.randomUUID().slice(0, 8)}.`;
  let disputeId = '';

  try {
    let merchantName = '';
    await test.step('a customer submits a fresh dispute', async () => {
      await signIn(customer, DEMO.customer);
      const tx = await pickDisputable(customer);
      merchantName = tx.merchantName;
      disputeId = await submitViaForm(customer, tx.id, description);
      await expect(customer.getByText('Submitted', { exact: true }).first()).toBeVisible();
    });

    const transitions = trackTransitions(agent);

    await test.step('a different session signs in as the agent and finds it in the review queue', async () => {
      await signIn(agent, DEMO.agent);
      await agent.getByRole('navigation', { name: 'Primary' }).getByRole('link', { name: 'Queue' }).click();
      await expect(agent.getByRole('heading', { name: 'Review queue' })).toBeVisible();

      const { link, row } = await findInQueue(agent, disputeId);
      await expect(row).toContainText(merchantName);
      await expect(row).toContainText(DEMO.customer.name);
      await expect(row.getByText('Submitted', { exact: true })).toBeVisible();
      await expectClean(agent, agentViolations);

      await link.click();
      await expect(agent).toHaveURL(new RegExp(`/agent/disputes/${disputeId}$`));
      await expect(agent.getByRole('heading', { level: 1 })).toContainText(merchantName);
      await expect(agent.getByText(description)).toBeVisible();
      await expect(agent.getByText(DEMO.customer.name)).toBeVisible();
      await expect(agent.getByText('Submitted', { exact: true }).first()).toBeVisible();
      await expectClean(agent, agentViolations);
    });

    await test.step('start review needs no note and sends the version it was decided on', async () => {
      await agent.getByRole('button', { name: 'Start review' }).click();
      await expect(agent.getByRole('status').filter({ hasText: 'Status updated.' })).toBeVisible();
      await expect(agent.getByText('Under review', { exact: true }).first()).toBeVisible();
      await expect(agent.getByRole('button', { name: 'Start review' })).toHaveCount(0);

      expect(transitions).toHaveLength(1);
      expect(transitions[0]!.postDataJSON()).toEqual({
        toStatus: 'UnderReview',
      });
      expect((await transitions[0]!.allHeaders())['if-match']).toMatch(/^"\d+"$/);
    });

    await test.step('accepting without a note shows an inline error and sends no request', async () => {
      const note = agent.getByLabel(/^Note to the customer/);
      for (const attempt of ['', '   ']) {
        await note.fill(attempt);
        await agent.getByRole('button', { name: 'Accept', exact: true }).click();
        await expect(agent.getByText(NOTE_REQUIRED)).toBeVisible();
        await expect(note).toHaveAttribute('aria-invalid', 'true');
        await expect(note).toBeFocused(); // focus moves to the field that needs fixing
        await expect(agent.getByRole('group', { name: 'Confirm decision' })).toHaveCount(0);
      }
      expect(transitions).toHaveLength(1); // still only the start-review request
    });

    await test.step('with a note, accepting asks to confirm; cancelling sends nothing', async () => {
      await agent.getByLabel(/^Note to the customer/).fill(NOTE);
      await agent.getByRole('button', { name: 'Accept', exact: true }).click();
      const confirm = agent.getByRole('group', { name: 'Confirm decision' });
      await expect(confirm.getByText('Do you want to accept this dispute?')).toBeVisible();
      await expect(agent.getByText(NOTE_REQUIRED)).toHaveCount(0);
      await expectClean(agent, agentViolations);

      await confirm.getByRole('button', { name: 'Cancel' }).click();
      await expect(confirm).toHaveCount(0);
      expect(transitions).toHaveLength(1);
    });

    let agentSubject = '';
    await test.step('confirming accepts the dispute and records the agent in the agent view', async () => {
      await agent.getByRole('button', { name: 'Accept', exact: true }).click();
      await agent.getByRole('button', { name: 'Yes, accept' }).click();
      await expect(agent.getByRole('status').filter({ hasText: 'Status updated.' })).toBeVisible();
      await expect(agent.getByText('Accepted', { exact: true }).first()).toBeVisible();
      await expect(agent.getByText('This dispute is decided')).toBeVisible();
      await expect(agent.getByRole('button', { name: 'Accept', exact: true })).toHaveCount(0);

      expect(transitions).toHaveLength(2);
      expect(transitions[1]!.postDataJSON()).toEqual({
        toStatus: 'Accepted',
        note: NOTE,
      });
      expect((await transitions[1]!.allHeaders())['if-match']).toMatch(/^"\d+"$/);

      // The agent's timeline names the acting agent (shortened), the customer's does not (below).
      const view = await api(agent, 'GET', `/api/v1/agent/disputes/${disputeId}`);
      expect(view.status).toBe(200);
      const agentEntries = view.body.timeline.filter((e: { actorType: string }) => e.actorType === 'Agent');
      expect(agentEntries).toHaveLength(2);
      agentSubject = agentEntries[0].actorId;
      expect(agentSubject).toMatch(new RegExp(`^${GUID}$`));
      expect(agentEntries[1].actorId).toBe(agentSubject);
      await expect(
        agent.getByText(`Agent ${agentSubject.slice(0, 8)} changed the status from Under review to Accepted`),
      ).toBeVisible();
      await expectClean(agent, agentViolations);
    });

    await test.step('the customer reloads and sees the resolution, the note and "Back-office", never who', async () => {
      await customer.reload();
      await expect(customer.getByRole('heading', { level: 1 })).toContainText(merchantName);
      await expect(customer.getByText('Accepted', { exact: true }).first()).toBeVisible();
      await expect(customer.getByText('Resolution note')).toBeVisible();
      await expect(customer.getByText(NOTE, { exact: true }).first()).toBeVisible();
      await expect(customer.getByRole('button', { name: 'Withdraw dispute' })).toHaveCount(0);

      const timeline = customer.getByRole('list', { name: 'Timeline' });
      await expect(timeline.getByText('You submitted this dispute')).toBeVisible();
      await expect(timeline.getByText('Back-office changed the status from Submitted to Under review')).toBeVisible();
      await expect(timeline.getByText('Back-office changed the status from Under review to Accepted')).toBeVisible();
      await expect(timeline.getByText(`Note: ${NOTE}`)).toBeVisible();

      // Nothing on the page names the agent: not the id (whole or shortened), not the display name.
      const pageText = await customer.locator('body').innerText();
      expect(pageText).not.toContain(agentSubject);
      expect(pageText).not.toContain(agentSubject.slice(0, 8));
      expect(pageText).not.toContain('Anele');
      expect(pageText).not.toMatch(/\bAgent\b/);

      // Nor does the customer API: the kind of actor is there, the actor is not.
      const view = await api(customer, 'GET', `/api/v1/disputes/${disputeId}`);
      expect(view.status).toBe(200);
      expect(view.body.status).toBe('Accepted');
      expect(view.body.timeline.map((e: { actorType: string }) => e.actorType)).toEqual(['Customer', 'Agent', 'Agent']);
      const raw = JSON.stringify(view.body);
      expect(raw).not.toContain(agentSubject);
      expect(raw).not.toMatch(/actorId/i);

      await expectClean(customer, customerViolations);
    });
  } finally {
    await withdrawIfOpen(customer, disputeId); // only matters if a step above failed part-way
    await Promise.all([customerContext.close(), agentContext.close()]);
  }
});

test('a decision made on a stale version is refused with a 412 and the agent sees the reloaded dispute', async ({ browser }) => {
  const customerContext = await browser.newContext();
  const agentContext = await browser.newContext();
  const customer = await customerContext.newPage();
  const first = await agentContext.newPage();
  const second = await agentContext.newPage();
  const firstViolations = await trackCspViolations(first);
  const secondViolations = await trackCspViolations(second);
  let disputeId = '';

  try {
    await signIn(customer, DEMO.customer2);
    disputeId = await submitViaApi(customer, (await pickDisputable(customer)).id);
    await signIn(first, DEMO.agent);

    // Both agent pages load the same version of the dispute.
    for (const page of [first, second]) {
      await page.goto(`/agent/disputes/${disputeId}`);
      await expect(page.getByRole('button', { name: 'Start review' })).toBeVisible();
    }

    await test.step('one page moves the dispute on', async () => {
      await first.getByRole('button', { name: 'Start review' }).click();
      await expect(first.getByRole('status').filter({ hasText: 'Status updated.' })).toBeVisible();
      await expect(first.getByText('Under review', { exact: true }).first()).toBeVisible();
    });

    await test.step('the other page, still on the old version, is refused and reloads', async () => {
      const draft = 'Draft note typed before the page went stale.';
      await second.getByLabel(/^Note to the customer/).fill(draft);

      const [response] = await Promise.all([
        second.waitForResponse((r) => r.request().method() === 'POST' && TRANSITIONS.test(new URL(r.url()).pathname)),
        second.getByRole('button', { name: 'Start review' }).click(),
      ]);
      expect(response.status()).toBe(412);
      expect((await response.request().allHeaders())['if-match']).toMatch(/^"\d+"$/);
      expect(response.headers()['content-type']).toContain('application/problem+json');
      expect((await response.json()).code).toBe('precondition_failed');
      expect(response.headers()['etag']).toMatch(/^"\d+"$/); // the current version
      expect(response.headers()['etag']).not.toBe((await response.request().allHeaders())['if-match']);

      // The page says what happened and shows the current dispute, so the agent decides on what is true now.
      await expect(
        second.getByRole('status').filter({
          hasText: 'This dispute changed while you were reviewing it. We reloaded it.',
        }),
      ).toBeVisible();
      await expect(second.getByText('Under review', { exact: true }).first()).toBeVisible();
      await expect(second.getByRole('button', { name: 'Start review' })).toHaveCount(0);
      await expect(second.getByRole('button', { name: 'Accept', exact: true })).toBeVisible();
      await expect(second.getByRole('button', { name: 'Reject', exact: true })).toBeVisible();
      await expect(second.getByLabel(/^Note to the customer/)).toHaveValue(draft); // the typed note survives

      expect(await secondViolations()).toEqual([]);
      const axe = await new AxeBuilder({ page: second }).analyze();
      expect(axe.violations).toEqual([]);
    });

    await test.step('the refused request changed nothing: one move to Under review, made once', async () => {
      const view = await api(first, 'GET', `/api/v1/agent/disputes/${disputeId}`);
      expect(view.body.status).toBe('UnderReview');
      expect(view.body.timeline.map((e: { to: string }) => e.to)).toEqual(['Submitted', 'UnderReview']);
    });

    expect(await firstViolations()).toEqual([]);
  } finally {
    await withdrawIfOpen(customer, disputeId);
    await Promise.all([customerContext.close(), agentContext.close()]);
  }
});
