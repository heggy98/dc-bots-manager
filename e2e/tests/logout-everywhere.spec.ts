import { expect, newLoggedInPage, refreshStatus, test } from './support';

// Runs in the last project: it revokes every session, including the shared admin storage state.
test('log out everywhere ends sessions on all devices', async ({ browser }) => {
  const deviceA = await newLoggedInPage(browser);
  const deviceB = await newLoggedInPage(browser);
  expect((await deviceB.page.request.get('/api/auth/me')).status()).toBe(200);

  deviceA.page.once('dialog', dialog => dialog.accept());
  await deviceA.page.getByRole('button', { name: 'Log out everywhere' }).click();
  await expect(deviceA.page).not.toHaveURL(/\/admin$/);

  // Device B: both its access token and its refresh token are revoked.
  await expect.poll(async () => (await deviceB.page.request.get('/api/auth/me')).status()).toBe(401);
  expect(await refreshStatus(deviceB.page)).toBe(401);

  // The SPA on device B notices on its next API call and sends the user to the login page.
  await deviceB.page.reload();
  await expect(deviceB.page).toHaveURL(/\/login/);

  await deviceA.context.close();
  await deviceB.context.close();
});
