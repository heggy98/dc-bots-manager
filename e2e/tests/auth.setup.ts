import { test as setup } from '@playwright/test';

import { ADMIN_STATE, newLoggedInPage } from './support';

/** Logs in once and stores the admin session (HttpOnly cookies + local session hint) for the other specs. */
setup('authenticate as admin', async ({ browser }) => {
  const { context } = await newLoggedInPage(browser);
  await context.storageState({ path: ADMIN_STATE });
  await context.close();
});
