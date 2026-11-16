import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Browser, type Page, type Request } from '@playwright/test';

import { api, DEMO, pickDisputable, signIn, submitViaApi, trackCspViolations, withdrawIfOpen } from './support';

// This file is named to sort after every other spec on purpose: Playwright runs files alphabetically, and dispute
// submits are limited to 10 a minute per customer (a fixed window). Run early, these submits used up the window that
// disputes.spec's own submits need; run last, the worst case is that submitViaApi waits for a fresh window.
test.describe.configure({ timeout: 120_000 }); // room for that wait

/**
 * Dispute assignment (ADR-019), at the API level against the real stack: take next hands concurrent agents different
 * disputes, only the reviewing agent may decide or release, an agent can still claim one dispute directly, and the
 * customer never sees who is reviewing. The stack is shared (it may hold someone's demo data), so this file never claims
 * or releases a dispute it did not submit: take next claims whatever is oldest, so its race runs only when the unclaimed
 * queue holds nothing but this test's own disputes, and is skipped otherwise. The workbench test below follows the same
 * rule for its Take next press.
 */

async function pages(browser: Browser, count: number) {
  const contexts = await Promise.all(Array.from({ length: count }, () => browser.newContext()));
  return { contexts, pages: await Promise.all(contexts.map((context) => context.newPage())) };
}

async function subjectOf(page: Page): Promise<string> {
  const me = await api(page, 'GET', '/api/v1/me');
  expect(me.status).toBe(200);
  return me.body.subject as string;
}

/** Release a dispute this test submitted, if the agent still holds it. */
async function releaseIfHeld(page: Page, id: string) {
  const current = await api(page, 'GET', `/api/v1/agent/disputes/${id}`).catch(() => null);
  if (current?.status !== 200 || current.body.status !== 'UnderReview' || current.body.assignment !== 'You') return;
  await api(page, 'POST', `/api/v1/agent/disputes/${id}/transitions`, {
    headers: { 'If-Match': current.etag! },
    json: { toStatus: 'Submitted' },
  }).catch(() => null);
}

test('two agents taking next at the same moment get different disputes', async ({ browser }) => {
  const { contexts, pages: opened } = await pages(browser, 3);
  const [customer, first, second] = opened as [Page, Page, Page];
  const submitted: string[] = [];
  const held: { page: Page; id: string }[] = [];

  try {
    await signIn(customer, DEMO.customer);
    for (let i = 0; i < 2; i++) submitted.push(await submitViaApi(customer, (await pickDisputable(customer)).id));
    await signIn(first, DEMO.agent);
    await signIn(second, DEMO.agent2);
    const [firstSubject, secondSubject] = await Promise.all([first, second].map(subjectOf));

    const unclaimed = await api(first, 'GET', '/api/v1/agent/disputes?assignment=unclaimed&limit=100');
    expect(unclaimed.status).toBe(200);
    const foreign = (unclaimed.body.items as { id: string }[]).filter((item) => !submitted.includes(item.id));
    test.skip(
      foreign.length > 0 || unclaimed.body.nextCursor !== null,
      `the unclaimed queue holds ${foreign.length}${unclaimed.body.nextCursor === null ? '' : '+'} dispute(s) this test did not ` +
        'submit; take next would claim them, so the race runs only on a stack whose unclaimed queue is otherwise empty',
    );

    const results = await Promise.all([first, second].map((page) => api(page, 'POST', '/api/v1/agent/disputes/take-next')));
    for (const [index, result] of results.entries()) {
      if (result.status === 200 && submitted.includes(result.body.id as string)) {
        held.push({ page: index === 0 ? first : second, id: result.body.id as string });
      }
    }
    for (const result of results) {
      expect(result.status).toBe(200);
      expect(result.etag).toMatch(/^"\d+"$/);
      expect(submitted, 'take next claimed a dispute this test did not submit (one arrived after the check)').toContain(result.body.id);
    }
    expect(results[0]!.body.id).not.toBe(results[1]!.body.id);
    expect(results.map((r) => [r.body.status, r.body.assignment, r.body.assigneeId])).toEqual([
      ['UnderReview', 'You', firstSubject],
      ['UnderReview', 'You', secondSubject],
    ]);
  } finally {
    for (const { page, id } of held) await releaseIfHeld(page, id);
    for (const id of submitted) await withdrawIfOpen(customer, id);
    await Promise.all(contexts.map((context) => context.close()));
  }
});

test('only the reviewing agent may decide or release, and the customer never sees who', async ({ browser }) => {
  const { contexts, pages: opened } = await pages(browser, 3);
  const [customer, reviewer, colleague] = opened as [Page, Page, Page];
  let id = '';

  try {
    await signIn(customer, DEMO.customer);
    id = await submitViaApi(customer, (await pickDisputable(customer)).id);
    await signIn(reviewer, DEMO.agent3);
    await signIn(colleague, DEMO.agent2);
    const [reviewerSubject, colleagueSubject] = await Promise.all([reviewer, colleague].map(subjectOf));

    await test.step('an agent claims one particular dispute directly', async () => {
      const view = await api(reviewer, 'GET', `/api/v1/agent/disputes/${id}`);
      expect(view.body.assignment).toBe('Unclaimed');

      const claimed = await api(reviewer, 'POST', `/api/v1/agent/disputes/${id}/transitions`, {
        headers: { 'If-Match': view.etag! },
        json: { toStatus: 'UnderReview' },
      });
      expect(claimed.status).toBe(200);
      expect([claimed.body.assignment, claimed.body.assigneeId]).toEqual(['You', reviewerSubject]);
    });

    await test.step('another agent cannot decide or release it: 409 not_assignee, nothing changes', async () => {
      const before = await api(reviewer, 'GET', `/api/v1/agent/disputes/${id}`);
      const fresh = await api(colleague, 'GET', `/api/v1/agent/disputes/${id}`);
      expect(fresh.body.assignment).toBe('AnotherAgent');
      expect(fresh.body.assigneeId).toBe(reviewerSubject);

      const accept = await api(colleague, 'POST', `/api/v1/agent/disputes/${id}/transitions`, {
        headers: { 'If-Match': fresh.etag! },
        json: { toStatus: 'Accepted', note: 'Merchant confirmed the duplicate.' },
      });
      const release = await api(colleague, 'POST', `/api/v1/agent/disputes/${id}/transitions`, {
        headers: { 'If-Match': fresh.etag! },
        json: { toStatus: 'Submitted' },
      });
      for (const refused of [accept, release]) {
        expect(refused.status).toBe(409);
        expect(refused.type).toContain('application/problem+json');
        expect(refused.body.code).toBe('not_assignee');
        expect(JSON.stringify(refused.body)).not.toContain(reviewerSubject);
      }

      const after = await api(reviewer, 'GET', `/api/v1/agent/disputes/${id}`);
      expect(after.etag).toBe(before.etag);
      expect([after.body.status, after.body.assigneeId, after.body.timeline.length]).toEqual([
        'UnderReview',
        reviewerSubject,
        before.body.timeline.length,
      ]);
    });

    await test.step('the customer sees the review, never who is doing it', async () => {
      const mine = await api(customer, 'GET', `/api/v1/disputes/${id}`);
      expect(mine.status).toBe(200);
      expect(mine.body.status).toBe('UnderReview');
      const raw = JSON.stringify(mine.body);
      expect(raw).not.toMatch(/assign/i);
      for (const subject of [reviewerSubject, colleagueSubject]) expect(raw).not.toContain(subject);
    });
  } finally {
    if (id) await releaseIfHeld(reviewer, id);
    await withdrawIfOpen(customer, id);
    await Promise.all(contexts.map((context) => context.close()));
  }
});

/** Axe evaluates strings in the page, which the CSP blocks by design: read CSP violations first. */
async function expectClean(page: Page, violations: () => Promise<string[]>) {
  expect(await violations()).toEqual([]);
  const axe = await new AxeBuilder({ page }).analyze();
  expect(axe.violations).toEqual([]);
}

/** A workbench section is keyset-paged: the dispute may be behind its "Load more". */
async function findInSection(page: Page, name: string, id: string) {
  const section = page.getByRole('region', { name, exact: true });
  const link = section.locator(`a[href$="/agent/disputes/${id}"]`);
  await expect(section.getByRole('status', { name: 'Result count' })).not.toHaveText(/^Loading/);
  for (let i = 0; i < 20 && (await link.count()) === 0; i++) {
    const more = section.getByRole('button', { name: 'Load more' });
    if (!(await more.isVisible().catch(() => false))) break;
    const before = await section.locator('tbody tr').count();
    await more.click();
    await expect.poll(async () => section.locator('tbody tr').count()).toBeGreaterThan(before);
  }
  await expect(link).toBeVisible();
  return link;
}

test('the workbench: take next lands under "In review by you", a colleague can only read it, and after release can claim it', async ({
  browser,
}) => {
  const { contexts, pages: opened } = await pages(browser, 3);
  const [customer, agent, colleague] = opened as [Page, Page, Page];
  const agentViolations = await trackCspViolations(agent);
  const colleagueViolations = await trackCspViolations(colleague);
  const releases: Request[] = [];
  agent.on('request', (r) => {
    if (r.method() === 'POST' && /\/api\/v1\/agent\/disputes\/[^/]+\/transitions$/.test(new URL(r.url()).pathname)) {
      releases.push(r);
    }
  });
  let id = '';
  let held = false;
  let taken = '';

  try {
    await signIn(customer, DEMO.customer);
    const submitted = await submitViaApi(customer, (await pickDisputable(customer)).id);
    id = submitted; // set at once: a skip below still withdraws it
    await signIn(agent, DEMO.agent);
    await signIn(colleague, DEMO.agent2);
    const agentSubject = await subjectOf(agent);
    const colleagueSubject = await subjectOf(colleague);

    const unclaimed = await api(agent, 'GET', '/api/v1/agent/disputes?assignment=unclaimed&limit=100');
    expect(unclaimed.status).toBe(200);
    const foreign = (unclaimed.body.items as { id: string }[]).filter((item) => item.id !== submitted);
    test.skip(
      foreign.length > 0 || unclaimed.body.nextCursor !== null,
      `the unclaimed queue holds ${foreign.length}${unclaimed.body.nextCursor === null ? '' : '+'} dispute(s) this test did not ` +
        'submit; Take next would claim them, so this runs only on a stack whose unclaimed queue is otherwise empty',
    );

    await test.step('Take next, the only crimson control on Home, opens the claimed dispute', async () => {
      await agent.goto('/');
      const take = agent.getByRole('button', { name: 'Take next' });
      await expect(take).toBeVisible();
      await expect(agent.locator('main .bg-primary')).toHaveCount(1);
      await expectClean(agent, agentViolations);

      await take.click();
      await expect(agent).toHaveURL(/\/agent\/disputes\/[0-9a-f-]{36}$/);
      taken = agent.url().split('/').pop()!;
      held = taken === id;
      expect(taken, 'Take next claimed a dispute this test did not submit (one arrived after the skip check); cleanup hands it back to the queue').toBe(id);
      await expect(agent.getByText('In review by you', { exact: true })).toBeVisible();
      await expect(agent.getByRole('button', { name: 'Release to queue' })).toBeVisible();
      await expectClean(agent, agentViolations);
    });

    await test.step('back on Home, the dispute is listed under "In review by you"', async () => {
      await agent.goto('/');
      const link = await findInSection(agent, 'In review by you', id);
      await expect(link).toContainText('Review');
      await expectClean(agent, agentViolations);
    });

    await test.step('a colleague sees it under "Claimed by other agents" and can only read it', async () => {
      await colleague.goto('/');
      const link = await findInSection(colleague, 'Claimed by other agents', id);
      await expect(link).toContainText('Read');
      await expectClean(colleague, colleagueViolations);

      await link.click();
      await expect(colleague).toHaveURL(new RegExp(`/agent/disputes/${id}$`));
      await expect(colleague.getByText(`In review by Agent ${agentSubject.slice(0, 8)}`, { exact: true })).toBeVisible();
      await expect(colleague.getByText('Another agent is reviewing this dispute. You can read it but not decide it.')).toBeVisible();
      for (const name of ['Accept', 'Reject', 'Release to queue', 'Start review']) {
        await expect(colleague.getByRole('button', { name, exact: true })).toHaveCount(0);
      }
      await expect(colleague.locator('body')).not.toContainText(agentSubject);
      await expectClean(colleague, colleagueViolations);
    });

    await test.step('the reviewing agent releases it: confirmed, no note, with If-Match', async () => {
      // The agent was last on Home (step 2): open the dispute again.
      await agent.goto(`/agent/disputes/${id}`);
      await expect(agent.getByText('In review by you', { exact: true })).toBeVisible();
      await agent.getByRole('button', { name: 'Release to queue' }).click();
      await agent.getByRole('button', { name: 'Yes, release to queue' }).click();
      await expect(agent.getByText('Released. The dispute is back in the queue for any agent.')).toBeVisible();
      held = false;
      await expect(agent.getByText('Unclaimed', { exact: true })).toBeVisible();

      expect(releases).toHaveLength(1);
      expect(releases[0]!.postDataJSON()).toEqual({ toStatus: 'Submitted' });
      expect((await releases[0]!.allHeaders())['if-match']).toMatch(/^"\d+"$/);
      await expect(agent.getByText(`Agent ${agentSubject.slice(0, 8)} released this dispute to the queue`)).toBeVisible();
      await expectClean(agent, agentViolations);
    });

    await test.step('the colleague, on reload, may now start the review', async () => {
      await colleague.reload();
      await expect(colleague.getByText('Unclaimed', { exact: true })).toBeVisible();
      await expect(colleague.getByRole('button', { name: 'Start review' })).toBeVisible();
      await expectClean(colleague, colleagueViolations);
    });

    await test.step('the customer is told it went back to the queue, never by whom', async () => {
      await customer.goto(`/disputes/${id}`);
      await expect(customer.getByText('Back-office returned this dispute to the review queue')).toBeVisible();
      for (const subject of [agentSubject, colleagueSubject]) {
        await expect(customer.locator('body')).not.toContainText(subject.slice(0, 8));
      }
    });
  } finally {
    if (held) await releaseIfHeld(agent, id);
    // A dispute arrived between the skip check and the press, so Take next claimed someone else's
    // and the test has already failed on it. The rule is to never touch a dispute this test did not
    // create, but leaving it claimed by this test's agent is the worse harm: hand it back to the
    // queue, so the stack is as it was before the press.
    if (taken && taken !== id) await releaseIfHeld(agent, taken);
    await withdrawIfOpen(customer, id);
    await Promise.all(contexts.map((context) => context.close()));
  }
});
