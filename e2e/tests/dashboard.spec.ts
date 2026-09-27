import { PRIVATE_BOT, PUBLIC_BOT, botCard, expect, test } from './support';

test.describe('admin dashboard', () => {
  test('lists the seeded bots', async ({ page }) => {
    await page.goto('/admin');

    await expect(page.getByRole('heading', { name: 'Admin Dashboard' })).toBeVisible();
    await expect(page.getByRole('heading', { name: PUBLIC_BOT, exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: PRIVATE_BOT, exact: true })).toBeVisible();
    await expect(botCard(page, PRIVATE_BOT).getByRole('button', { name: 'Start Bot' })).toBeVisible();
  });

  test('opens the bot detail page', async ({ page }) => {
    await page.goto('/admin');
    await botCard(page, PRIVATE_BOT).getByRole('button', { name: 'Manage & Configure' }).click();

    await expect(page).toHaveURL(/\/admin\/bot\/\d+$/);
    await expect(page.getByRole('heading', { name: PRIVATE_BOT, level: 2 })).toBeVisible();
    await expect(page.getByRole('link', { name: '← Back to Dashboard' })).toBeVisible();
  });

  test('session survives a reload', async ({ page }) => {
    await page.goto('/admin');
    await expect(page.getByRole('heading', { name: 'Admin Dashboard' })).toBeVisible();

    await page.reload();

    await expect(page).toHaveURL(/\/admin$/);
    await expect(page.getByRole('heading', { name: 'Admin Dashboard' })).toBeVisible();
    await expect(page.getByRole('heading', { name: PRIVATE_BOT, exact: true })).toBeVisible();
    expect((await page.request.get('/api/auth/me')).status()).toBe(200);
  });
});
