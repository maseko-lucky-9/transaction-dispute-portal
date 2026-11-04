import { expect, type Page } from '@playwright/test';

export const DEMO = {
  customer: { user: 'thandi', password: 'Thandi-Demo-2026!', name: 'Thandi Nkosi' },
  customer2: { user: 'sipho', password: 'Sipho-Demo-2026!', name: 'Sipho Dlamini' },
  agent: { user: 'agent', password: 'Agent-Demo-2026!', name: 'Anele Agent' },
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
