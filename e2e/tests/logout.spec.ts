import { EMPTY_STATE, expect, newLoggedInPage, refreshStatus, test } from './support';

test.use({ storageState: EMPTY_STATE });

test('logout ends the session', async ({ browser }) => {
  // Own session, so the shared admin storage state stays valid for the other specs.
  const { context, page } = await newLoggedInPage(browser);

  await page.getByRole('button', { name: 'Logout', exact: true }).click();

  await expect(page).toHaveURL(/\/$/);
  await expect(page.getByRole('link', { name: 'Login' })).toBeVisible();
  await expect.poll(async () => (await page.request.get('/api/auth/me')).status()).toBe(401);
  expect(await refreshStatus(page)).toBe(401);

  await page.goto('/admin');
  await expect(page).toHaveURL(/\/login\?returnUrl=%2Fadmin$/);
  await context.close();
});
