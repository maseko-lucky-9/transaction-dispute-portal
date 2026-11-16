import { expect, test, type Browser, type Page } from '@playwright/test';

import { api, DEMO, pickDisputable, signIn, submitViaApi, withdrawIfOpen } from './support';

/**
 * Dispute assignment (ADR-019), at the API level against the real stack: take next hands concurrent agents different
 * disputes, only the reviewing agent may decide or release, an agent can still claim one dispute directly, and the
 * customer never sees who is reviewing. The stack is shared (it may hold someone's demo data), so this file never claims
 * or releases a dispute it did not submit: take next claims whatever is oldest, so its race runs only when the unclaimed
 * queue holds nothing but this test's own disputes, and is skipped otherwise.
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
