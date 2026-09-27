import { EMPTY_STATE, adminCredentials, expect, submitLogin, test } from './support';

test.use({ storageState: EMPTY_STATE });

test.describe('login', () => {
  test('wrong password shows an error and stays on the login page', async ({ page }) => {
    await page.goto('/login');
    await submitLogin(page, adminCredentials().email, 'definitely-not-the-password');

    await expect(page.getByText('Invalid credentials').first()).toBeVisible();
    await expect(page).toHaveURL(/\/login/);
    expect((await page.request.get('/api/auth/me')).status()).toBe(401);
  });

  test('successful login returns to the requested page', async ({ page }) => {
    await page.goto('/admin/logs');
    await expect(page).toHaveURL(/\/login\?returnUrl=%2Fadmin%2Flogs$/);

    const { email, password } = adminCredentials();
    await submitLogin(page, email, password);

    await expect(page).toHaveURL(/\/admin\/logs$/);
    await expect(page.getByRole('link', { name: 'Dashboard' })).toBeVisible();
  });
});
