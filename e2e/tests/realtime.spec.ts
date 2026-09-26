import { PRIVATE_BOT, botCard, expect, test } from './support';

test.describe('real-time events', () => {
  test('SignalR connects over a WebSocket and auth cookies stay HttpOnly', async ({ page, context }) => {
    await page.goto('/admin');

    const hubSocket = page.waitForEvent('websocket', ws => ws.url().includes('/hubs/bot-events'));
    await botCard(page, PRIVATE_BOT).getByRole('button', { name: 'Manage & Configure' }).click();
    const ws = await hubSocket;

    // The SignalR handshake response ("{}" + record separator) proves the authenticated hub accepted us.
    const handshake = await ws.waitForEvent('framereceived', { timeout: 10_000 });
    expect(String(handshake.payload)).toContain('{}');
    expect(ws.isClosed()).toBe(false);

    // Tokens are only in HttpOnly cookies: page scripts cannot read them.
    expect(await page.evaluate(() => document.cookie)).toBe('');
    const authCookies = (await context.cookies()).filter(c => /access|refresh|auth/i.test(c.name));
    expect(authCookies.length).toBeGreaterThan(0);
    for (const cookie of authCookies) {
      expect(cookie.httpOnly, `${cookie.name} must be HttpOnly`).toBe(true);
    }
  });
});
