import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { authUrl } from '../playwright.config';
import { DEMO, signIn, signOut, trackCspViolations } from './support';

test('anonymous visitor sees Sign in; /bff/user is a 401 ProblemDetails, not a redirect', async ({ page, request }) => {
  await page.goto('/');
  await expect(page.getByRole('button', { name: /sign in/i })).toBeVisible();

  const res = await request.get('/bff/user', { maxRedirects: 0, headers: { 'X-CSRF': '1' } });
  expect(res.status()).toBe(401);
  expect(res.headers()['content-type']).toContain('application/problem+json');
  expect((await res.json()).code).toBeTruthy();

  const api = await request.get('/api/v1/me', { maxRedirects: 0, headers: { 'X-CSRF': '1' } });
  expect(api.status()).toBe(401);
});

test('customer signs in through Keycloak, sees the shell, no CSP violations, no axe violations', async ({ page }) => {
  const violations = await trackCspViolations(page);
  await signIn(page, DEMO.customer);

  const user = await page.evaluate(async () => (await fetch('/bff/user', { headers: { 'X-CSRF': '1' } })).json());
  expect(user.roles).toContain('customer');
  expect(user.roles).not.toContain('dispute-agent');
  expect(JSON.stringify(user)).not.toMatch(/token/i);

  // Session cookie is HttpOnly (invisible to JS) and no token is reachable from the page.
  const jsCookies = await page.evaluate(() => document.cookie);
  expect(jsCookies).not.toContain('dp_session');
  const stored = await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }));
  expect(stored).not.toMatch(/token|eyJ/);

  // Read CSP violations BEFORE axe runs: axe-core evaluates strings in the page, which a strict CSP rightly blocks.
  expect(await violations()).toEqual([]);
  const axe = await new AxeBuilder({ page }).analyze();
  expect(axe.violations).toEqual([]);
});

test('CSRF header is required on /api', async ({ page }) => {
  await signIn(page, DEMO.customer);
  const status = await page.evaluate(async () => (await fetch('/api/v1/me')).status);
  expect(status).toBe(403);
  const ok = await page.evaluate(async () => (await fetch('/api/v1/me', { headers: { 'X-CSRF': '1' } })).status);
  expect(ok).toBe(200);
});

test('after logout the password is asked for again (Keycloak session really ended)', async ({ page }) => {
  await signIn(page, DEMO.customer);
  await signOut(page);
  await expect(page.getByRole('button', { name: /sign in/i })).toBeVisible();
  await page.getByRole('button', { name: /sign in/i }).click();
  await expect(page.locator('#username')).toBeVisible(); // not auto-signed-in
  await expect(page.locator('#password')).toBeVisible();
});

test('the agent role is separate from the customer role', async ({ page }) => {
  await signIn(page, DEMO.agent);
  const user = await page.evaluate(async () => (await fetch('/bff/user', { headers: { 'X-CSRF': '1' } })).json());
  expect(user.roles).toContain('dispute-agent');
  expect(user.roles).not.toContain('customer');
});

test('edge: security headers per location, no-store on the API, Keycloak admin is not exposed', async ({ request }) => {
  const root = await request.get('/');
  const csp = root.headers()['content-security-policy'];
  expect(csp).toContain("default-src 'none'");
  expect(csp).toContain("frame-ancestors 'none'");
  expect(root.headers()['x-content-type-options']).toBe('nosniff');
  expect(root.headers()['cache-control']).toContain('no-cache');

  const api = await request.get('/api/v1/me', { headers: { 'X-CSRF': '1' } });
  expect(api.headers()['cache-control']).toContain('no-store');
  expect(api.headers()['x-content-type-options']).toBe('nosniff');

  for (const path of [
    '/admin/master/console/',
    '/realms/master/.well-known/openid-configuration',
    '/realms/dispute-portal/account/',
    '/metrics',
    '/health',
  ]) {
    expect((await request.get(`${authUrl}${path}`)).status(), path).toBe(404);
  }
  const oidc = await request.get(`${authUrl}/realms/dispute-portal/.well-known/openid-configuration`);
  expect(oidc.status()).toBe(200);
  expect(oidc.headers()['x-content-type-options']).toBe('nosniff');
});
