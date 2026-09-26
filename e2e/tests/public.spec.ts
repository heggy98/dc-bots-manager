import { EMPTY_STATE, PRIVATE_BOT, PUBLIC_BOT, adminCredentials, expect, test } from './support';

test.use({ storageState: EMPTY_STATE });

test.describe('public page', () => {
  test('lists public bots without exposing the owner', async ({ page }) => {
    const publicBots = page.waitForResponse(r => r.url().includes('/api/bot/public') && r.request().method() === 'GET');
    await page.goto('/');

    const response = await publicBots;
    expect(response.status()).toBe(200);
    const { email } = adminCredentials();
    const body = await response.text();
    expect(body.toLowerCase()).not.toContain(email.toLowerCase());
    expect(body).not.toMatch(/ownerUserId|botToken/i);

    await expect(page.getByRole('heading', { name: 'BotManager', level: 1 })).toBeVisible();
    await expect(page.getByRole('heading', { name: PUBLIC_BOT })).toBeVisible();
    await expect(page.getByRole('heading', { name: PRIVATE_BOT })).toHaveCount(0);
    await expect(page.locator('body')).not.toContainText(email);
    await expect(page.getByRole('link', { name: 'Login' })).toBeVisible();
  });

  test('unauthenticated /admin redirects to /login with returnUrl', async ({ page }) => {
    await page.goto('/admin');
    await expect(page).toHaveURL(/\/login\?returnUrl=%2Fadmin$/);
    await expect(page.getByRole('heading', { name: 'Admin Login' })).toBeVisible();
  });
});
