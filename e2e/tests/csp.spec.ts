import { PRIVATE_BOT, botCard, expect, test } from './support';

// Behind nginx (docker compose) the SPA is served with a strict CSP; set E2E_EXPECT_CSP=1 there to
// also require the header (the Angular dev server does not send it).
const expectCspHeader = process.env.E2E_EXPECT_CSP === '1';

test.describe('content security policy', () => {
  test('main pages load without CSP violations', async ({ page, cspViolations }) => {
    const paths = ['/', '/login', '/admin', '/admin/logs', '/admin/config', '/admin/commands'];
    for (const path of paths) {
      const response = await page.goto(path);
      await page.waitForLoadState('networkidle');
      if (expectCspHeader) {
        expect(response?.headers()['content-security-policy'], `CSP header on ${path}`).toContain("default-src 'self'");
      }
    }

    await page.goto('/admin');
    await botCard(page, PRIVATE_BOT).getByRole('button', { name: 'Manage & Configure' }).click();
    await expect(page.getByRole('heading', { name: PRIVATE_BOT, level: 2 })).toBeVisible();
    await page.waitForLoadState('networkidle');

    expect(cspViolations).toEqual([]);
  });
});
