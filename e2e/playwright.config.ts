import { defineConfig, devices } from '@playwright/test';

const appPort = process.env.APP_PORT ?? '8080';
const authPort = process.env.AUTH_PORT ?? '8180';

export const appUrl = `http://localhost:${appPort}`;
export const authUrl = `http://localhost:${authPort}`;

// Chromium only: Chrome and Firefox treat http://localhost as a secure context; Safari may not (README).
export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: appUrl,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
