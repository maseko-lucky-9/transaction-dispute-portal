import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, test, type Page } from '@playwright/test';
import { authUrl } from '../playwright.config';
import { DEMO, signIn } from './support';

// ADR-011, OpenID Connect Back-Channel Logout. Keycloak ends the session and POSTs a signed logout token to the API's
// management port; the API deletes the session's tickets at once. The access token lives 5 minutes and the API only
// refreshes in its last 60 seconds, so a 401 seconds after a Keycloak-side logout can only come from the back channel.

const composeFile = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../docker-compose.yml');

/** The SPA's own session probe: 200 while the session lives, 401 once it is gone. */
async function sessionStatus(page: Page): Promise<number> {
  return page.evaluate(async () => (await fetch('/bff/user', { headers: { 'X-CSRF': '1' } })).status);
}

/** Keycloak's session id for this app session (the logout link carries it; it is the ID token's sid). */
async function keycloakSessionId(page: Page): Promise<string> {
  const user = await page.evaluate(async () => (await fetch('/bff/user', { headers: { 'X-CSRF': '1' } })).json());
  const sid = new URL(user.logoutUrl, 'http://app.invalid').searchParams.get('sid');
  expect(sid).toMatch(/^[0-9a-f-]{36}$/);
  return sid!;
}

/**
 * Deletes one Keycloak session through the admin API, from inside the keycloak container (the admin API is on no
 * published port). The admin password never leaves the container, and is not on a command line: kcadm reads it from
 * KC_CLI_PASSWORD, set from the secrets volume. Targets the compose project in COMPOSE_PROJECT_NAME, or the file's
 * default (`dispute-portal`, as in CI).
 */
function adminDeleteKeycloakSession(sid: string): void {
  const script = [
    'set -euo pipefail',
    'export KC_CLI_PASSWORD="$(cat /run/kc/admin_password)"',
    'kc=/opt/keycloak/bin/kcadm.sh',
    'cfg=$(mktemp)',
    'trap \'rm -f "$cfg"\' EXIT',
    '"$kc" config credentials --config "$cfg" --server http://localhost:8080 --realm master --user admin >/dev/null',
    '"$kc" delete "sessions/$1" --config "$cfg" -r dispute-portal',
  ].join('\n');
  try {
    execFileSync('docker', ['compose', '-f', composeFile, 'exec', '-T', 'keycloak', 'bash', '-c', script, 'kcadm', sid], {
      stdio: ['ignore', 'pipe', 'pipe'],
      timeout: 60_000,
    });
  } catch (error) {
    // kcadm's own words (no secret: the password is in the environment, never in its output or argv).
    const stderr = (error as { stderr?: Buffer }).stderr?.toString().trim() ?? '';
    throw new Error(`kcadm could not delete the Keycloak session: ${stderr || String(error)}`);
  }
}

test('a logout at Keycloak itself, not through the app, ends the app session on the next request', async ({ page }) => {
  await signIn(page, DEMO.customer);
  expect(await sessionStatus(page)).toBe(200);

  // Keycloak's end-session endpoint with no id_token_hint: it asks for confirmation, then ends its SSO session and calls
  // every client's back-channel logout URL before it answers. The app's /bff/logout is never involved.
  await page.goto(`${authUrl}/realms/dispute-portal/protocol/openid-connect/logout`);
  await page.locator('#kc-logout').click();
  await expect(page.getByText(/you are logged out/i).first()).toBeVisible();

  await page.goto('/');
  expect(await sessionStatus(page)).toBe(401);
  await expect(page.getByRole('button', { name: /sign in/i })).toBeVisible();
});

test('an admin ending the session in Keycloak ends the app session on the next request', async ({ page }) => {
  await signIn(page, DEMO.customer2);
  expect(await sessionStatus(page)).toBe(200);
  const sid = await keycloakSessionId(page);

  adminDeleteKeycloakSession(sid);

  // The very next request, with no reload and no wait: the ticket is already gone from Redis.
  expect(await sessionStatus(page)).toBe(401);
  await page.reload();
  await expect(page.getByRole('button', { name: /sign in/i })).toBeVisible();
});
