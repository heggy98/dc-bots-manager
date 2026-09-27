import { defineConfig, devices } from '@playwright/test';

/**
 * E2E suite for BotManager. Targets a running stack (docker compose on :8080 by default).
 * Required env: E2E_ADMIN_EMAIL, E2E_ADMIN_PASSWORD. Seed data first with `npm run seed`
 * (E2E_DB_CONTAINER / E2E_DB_PASSWORD point it at the SQL Server container), then `npm run e2e`.
 *
 * Local dev stack instead of compose: run the API with `dotnet run` (Auth__CookieSecure=false and
 * Cors__AllowedOrigins__0=<dev server origin>, the hub rejects foreign origins), start
 * `ng serve --proxy-config ../e2e/proxy.e2e.json` (edit the API target there) and set E2E_BASE_URL.
 * E2E_EXPECT_CSP=1 additionally requires the CSP header, which only nginx sends.
 *
 * The suite runs serially with a single worker: the API rate-limits logins per IP
 * (RateLimiting:AuthPermitLimit, 10/min by default) and "log out everywhere" revokes every
 * session, so that project runs last.
 */
const baseURL = process.env.E2E_BASE_URL ?? 'http://localhost:8080';
// Written by tests/auth.setup.ts (keep in sync with ADMIN_STATE in tests/support.ts).
const adminState = '.auth/admin.json';

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  timeout: 30_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI
    ? [['list'], ['html', { open: 'never' }], ['github']]
    : [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure'
  },
  projects: [
    {
      name: 'setup',
      testMatch: /auth\.setup\.ts/
    },
    {
      name: 'chromium',
      // Admin session by default; anonymous specs opt out with test.use({ storageState: EMPTY_STATE }).
      use: { ...devices['Desktop Chrome'], storageState: adminState },
      dependencies: ['setup'],
      testIgnore: /logout-everywhere\.spec\.ts/
    },
    {
      // Revokes all sessions (including the shared admin storage state), so it must run last.
      name: 'session-revocation',
      use: { ...devices['Desktop Chrome'] },
      dependencies: ['chromium'],
      testMatch: /logout-everywhere\.spec\.ts/
    }
  ]
});
