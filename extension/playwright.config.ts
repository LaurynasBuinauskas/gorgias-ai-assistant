import { defineConfig, devices } from '@playwright/test';

// The shell is the one piece of this system that runs inside someone else's page, so it is
// the one piece a unit test cannot honestly cover: what matters is whether an iframe lands in
// the right place in a real DOM, survives a SPA navigation, and never gets rebuilt.
//
// No webServer. Every origin the shell touches — the Gorgias page, the panel, the config and
// telemetry endpoints — is fulfilled by request interception, so the suite needs nothing
// running and cannot fail because a port was busy.
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? 'github' : 'list',
  use: {
    ...devices['Desktop Chrome'],
    trace: 'retain-on-failure',
  },
});
