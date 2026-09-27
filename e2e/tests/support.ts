import { test as base, expect, type Browser, type BrowserContext, type Page } from '@playwright/test';

/** Names of the bots inserted by scripts/seed.mjs (keep in sync with scripts/seed-data.mjs). */
export const PUBLIC_BOT = 'E2E Public Bot';
export const PRIVATE_BOT = 'E2E Private Bot';

export const ADMIN_STATE = '.auth/admin.json';

/** Storage state without any session (for anonymous specs). */
export const EMPTY_STATE = { cookies: [], origins: [] };

export function adminCredentials(): { email: string; password: string } {
  const email = process.env.E2E_ADMIN_EMAIL;
  const password = process.env.E2E_ADMIN_PASSWORD;
  if (!email || !password) {
    throw new Error('Set E2E_ADMIN_EMAIL and E2E_ADMIN_PASSWORD to the admin credentials of the stack under test.');
  }
  return { email, password };
}

/** Forces the English UI (the app defaults to Czech) so role/text locators are stable. */
export async function useEnglish(context: BrowserContext): Promise<void> {
  await context.addInitScript(() => window.localStorage.setItem('lang', 'en'));
}

/** Calls the session refresh endpoint like the SPA does (with the API's CSRF header). */
export async function refreshStatus(page: Page): Promise<number> {
  const response = await page.request.post('/api/auth/refresh', { headers: { 'X-Requested-With': 'BotManager' } });
  return response.status();
}

/** Fills and submits the login form (the page must already show it). */
export async function submitLogin(page: Page, email: string, password: string): Promise<void> {
  await page.getByLabel('Email Address').fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Sign In', exact: true }).click();
}

/** Logs in through the UI in a fresh context (for tests that end or revoke their own session). */
export async function newLoggedInPage(browser: Browser): Promise<{ context: BrowserContext; page: Page }> {
  const context = await browser.newContext();
  await useEnglish(context);
  const page = await context.newPage();
  await page.goto('/login');
  const { email, password } = adminCredentials();
  await submitLogin(page, email, password);
  await expect(page).toHaveURL(/\/admin$/);
  await expect(page.getByRole('heading', { name: 'Admin Dashboard' })).toBeVisible();
  return { context, page };
}

/** The dashboard card of a bot: the innermost element holding both its name and its manage button. */
export function botCard(page: Page, botName: string) {
  return page
    .locator('div')
    .filter({ has: page.getByRole('heading', { name: botName, exact: true }) })
    .filter({ has: page.getByRole('button', { name: 'Manage & Configure' }) })
    .last();
}

type Fixtures = {
  /** Collected CSP violations ("Refused to ..." console messages and securitypolicyviolation events). */
  cspViolations: string[];
};

/**
 * Base test: English UI and CSP violation collection on every page of the context.
 */
export const test = base.extend<Fixtures>({
  context: async ({ context }, use) => {
    await useEnglish(context);
    await use(context);
  },
  cspViolations: async ({ page }, use) => {
    const violations: string[] = [];
    page.on('console', msg => {
      if (msg.text().includes('Refused to')) violations.push(msg.text());
    });
    await page.exposeFunction('__e2eReportCspViolation', (text: string) => violations.push(text));
    await page.addInitScript(() => {
      document.addEventListener('securitypolicyviolation', e => {
        const report = (window as unknown as { __e2eReportCspViolation?: (t: string) => void }).__e2eReportCspViolation;
        report?.(`${e.violatedDirective} blocked ${e.blockedURI || 'inline'} (${e.sourceFile}:${e.lineNumber})`);
      });
    });
    await use(violations);
  }
});

export { expect };
