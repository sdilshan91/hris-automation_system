import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright E2E config for the HRM SaaS frontend.
 *
 * These tests drive the REAL browser → Angular → real API → real Postgres, which is the only layer that
 * catches FE↔BE contract drift (wrong URLs, missing form fields, route guards) that mocked unit tests miss.
 *
 * PREREQUISITES to run (`npm run e2e`):
 *   1. `npx playwright install chromium firefox webkit` (one-time, downloads the browser engines).
 *   2. The full dev stack running: backend on :5000 (rebuilt so the dev-only `e2e` tenant + owner@e2e.test
 *      login is seeded) and `ng serve` on :4200.
 * Multi-tenant in dev is selected via the `?tenant=e2e` query param (see auth fixture).
 *
 * CROSS-BROWSER: run a single browser with `npx playwright test --project=firefox` (or `webkit`,
 * `chromium`). Omit `--project` to run all three. The firefox/webkit engines ship with Playwright and
 * are already installed alongside chromium — no extra MCP server or download is needed for cross-browser
 * TCs (the Playwright MCP is chromium-only; cross-browser runs go through this test runner instead).
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: process.env.CI ? 'github' : 'list',
  timeout: 60_000,
  expect: { timeout: 10_000 },
  use: {
    // E2E_BASE_URL lets CI (and a local run on a spare port) point at a different origin. It must be an
    // origin that PROXIES /api to the backend: `ng serve` does via proxy.conf.json, but the Docker
    // `frontend` container on :4200 does NOT — its nginx.conf serves static files only, so GETs silently
    // return index.html and POSTs 405, and every test fails at login. See ISSUE-365.
    baseURL: process.env['E2E_BASE_URL'] ?? 'http://localhost:4200',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    actionTimeout: 15_000,
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
    { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
    { name: 'webkit', use: { ...devices['Desktop Safari'] } },
  ],

  // To have Playwright start the dev server itself, uncomment and adjust (it must also ensure the backend
  // is up — left off by default because the stack is run separately in this project):
  // webServer: {
  //   command: 'npm start',
  //   url: 'http://localhost:4200',
  //   reuseExistingServer: true,
  //   timeout: 120_000,
  // },
});
