import { defineConfig, devices } from '@playwright/test'

// End-to-end tests (spec 11.1). Scenarios live in e2e/<area>.spec.ts and import everything from e2e/fixtures.ts.
// Stage B: TASKLOOM_E2E_URL=http://localhost:<your vite port> TASKLOOM_E2E_STAGE=B npm run e2e -- e2e/<area>.spec.ts
// Stage C: the built app served by the API on :5080 (the default URL), then `npm run e2e`.
// Chromium comes from PLAYWRIGHT_BROWSERS_PATH; never run `playwright install`. Never point a run at taskloom_docker.
export default defineConfig({
  testDir: 'e2e',
  workers: 2,
  retries: process.env.CI ? 2 : 0,
  forbidOnly: !!process.env.CI,
  // The HTML report goes to playwright-report/ (git-ignored) and never opens a browser or a server by itself.
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.TASKLOOM_E2E_URL ?? 'http://localhost:5080',
    timezoneId: 'UTC',
    locale: 'en-US',
    permissions: ['clipboard-read', 'clipboard-write'],
    trace: 'retain-on-failure',
  },
  projects: [
    {
      name: 'desktop',
      use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } },
      grepInvert: /@phone/,
    },
    {
      name: 'phone',
      use: { browserName: 'chromium', viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true },
      grep: /@phone/,
    },
  ],
})
