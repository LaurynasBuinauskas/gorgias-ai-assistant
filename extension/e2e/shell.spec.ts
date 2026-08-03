import { readFileSync } from 'node:fs';
import { expect, type Page, test } from '@playwright/test';

/**
 * End-to-end cover for the shell's integration points, running the built bundle in a real
 * browser against a stand-in Gorgias page.
 *
 * The bundle is the artefact that ships, so that is what is loaded here rather than the
 * modules it was built from. `chrome.storage` is absent in a plain page, which the shell
 * already treats as "use the baked-in defaults" — so the code under test is unmodified.
 */

const SHELL = readFileSync(new URL('../dist/inject.js', import.meta.url), 'utf8');

const PANEL_ORIGIN = 'http://localhost:5173';
const API_ORIGIN = 'http://localhost:5249';
const GORGIAS = 'https://acme.gorgias.com';

/** Records every context message it receives, so the page can assert on what the panel saw. */
const PANEL_HTML = `<!doctype html><meta charset="utf-8"><title>panel</title>
<body><div id="received"></div><script>
  window.addEventListener('message', (event) => {
    const data = event.data;
    if (data && data.v === 1 && data.type === 'copilot:context') {
      const log = document.getElementById('received');
      log.textContent = (log.textContent ? log.textContent + ',' : '') + data.ticketId;
    }
  });
  parent.postMessage({ v: 1, type: 'copilot:ready' }, '*');
</script></body>`;

type Options = { readonly anchor?: boolean; readonly killSwitch?: boolean };

async function openTicketView(page: Page, path: string, options: Options = {}): Promise<void> {
  const anchorMarkup = options.anchor
    ? '<aside id="ticket-sidebar"></aside>'
    : '<aside id="something-else"></aside>';

  await page.route(`${GORGIAS}/app/**`, (route) =>
    route.fulfill({
      contentType: 'text/html',
      body: `<!doctype html><meta charset="utf-8"><title>Gorgias</title>
        <body><main id="conversation">ticket</main>${anchorMarkup}</body>`,
    }),
  );

  await page.route(`${API_ORIGIN}/v1/config`, (route) =>
    route.fulfill({
      contentType: 'application/json',
      body: JSON.stringify({
        v: 1,
        killSwitch: options.killSwitch ?? false,
        minShellVersion: '0.1.0',
        // Two probes, the first deliberately absent, so a hit proves the shell walks the list
        // rather than trying only the first selector.
        anchorProbes: ['#not-present', '#ticket-sidebar'],
        exemplars: true,
      }),
    }),
  );

  // Telemetry is fire-and-forget; swallow it so a test never depends on it.
  await page.route(`${API_ORIGIN}/v1/telemetry/**`, (route) => route.fulfill({ status: 204 }));
  await page.route(`${PANEL_ORIGIN}/**`, (route) =>
    route.fulfill({ contentType: 'text/html', body: PANEL_HTML }),
  );

  await page.goto(`${GORGIAS}${path}`);
  await page.addScriptTag({ content: SHELL });
}

const frame = '#copilot-panel-frame';

test('mounts a docked panel when an anchor probe matches', async ({ page }) => {
  await openTicketView(page, '/app/views/42/900123', { anchor: true });

  await expect(page.locator(frame)).toHaveClass(/copilot-docked/);
  // Docked means inside the anchor, not merely on the page — appending to body while
  // reporting "docked" would look identical to every assertion except this one.
  await expect(page.locator(`#ticket-sidebar > ${frame}`)).toHaveCount(1);
  await expect(page.locator('#copilot-panel-toggle')).toHaveCount(0);
});

test('falls back to floating with a toggle when no anchor is found', async ({ page }) => {
  await openTicketView(page, '/app/views/42/900123', { anchor: false });

  await expect(page.locator(frame)).toHaveClass(/copilot-floating/);
  await expect(page.locator(`body > ${frame}`)).toHaveCount(1);

  // Floating overlays the agent's work, so it must be dismissible.
  const toggle = page.locator('#copilot-panel-toggle');
  await expect(toggle).toBeVisible();
  await toggle.click();
  await expect(page.locator(frame)).toHaveClass(/copilot-hidden/);
  await toggle.click();
  await expect(page.locator(frame)).not.toHaveClass(/copilot-hidden/);
});

test('sends the ticket id from the URL, never from the page', async ({ page }) => {
  await openTicketView(page, '/app/views/42/900123', { anchor: true });

  await expect(page.frameLocator(frame).locator('#received')).toHaveText('900123');
});

test('reuses the same iframe across a ticket change', async ({ page }) => {
  await openTicketView(page, '/app/views/42/900123', { anchor: true });
  await expect(page.frameLocator(frame).locator('#received')).toHaveText('900123');

  // Mark the mounted frame. A remount loses the marker, which is the whole point: rebuilding
  // the iframe would throw away the panel's session and any in-flight draft.
  await page.locator(frame).evaluate((node) => node.setAttribute('data-original', 'yes'));

  await page.evaluate(() => history.pushState({}, '', '/app/views/42/900456'));

  await expect(page.frameLocator(frame).locator('#received')).toHaveText('900123,900456');
  await expect(page.locator(frame)).toHaveAttribute('data-original', 'yes');
  await expect(page.locator(frame)).toHaveCount(1);
});

test('hides the panel when navigating away from a ticket', async ({ page }) => {
  await openTicketView(page, '/app/views/42/900123', { anchor: true });
  await expect(page.locator(frame)).toHaveCount(1);

  await page.evaluate(() => history.pushState({}, '', '/app/views/42'));

  await expect(page.locator(frame)).toHaveClass(/copilot-hidden/);
});

test('mounts nothing at all when the kill switch is engaged', async ({ page }) => {
  await openTicketView(page, '/app/views/42/900123', { anchor: true, killSwitch: true });

  // Deliberately asserted after a settle: "not yet" and "never" look the same too early.
  await page.waitForTimeout(500);
  await expect(page.locator(frame)).toHaveCount(0);
  await expect(page.locator('#copilot-panel-toggle')).toHaveCount(0);
});

test('ignores a page that is not a ticket view', async ({ page }) => {
  await openTicketView(page, '/app/settings/profile', { anchor: true });

  await page.waitForTimeout(500);
  await expect(page.locator(frame)).toHaveCount(0);
});
