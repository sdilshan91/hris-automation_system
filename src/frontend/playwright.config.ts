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
 * DO NOT ADD A SHARED `storageState` (GAP-034, checked 2026-08-11 — it looks obvious and it is wrong here).
 * The plan proposed saving one logged-in state so the suite authenticates once instead of thirty times. This
 * app's auth design makes that unsafe:
 *   - the ACCESS token is deliberately in-memory only (auth.service.ts:41, XSS protection), so a saved state
 *     contains no usable session — only the `refreshToken` cookie;
 *   - the SPA does silently restore from that cookie at bootstrap (auth.service.ts:205, APP_INITIALIZER), so
 *     at first glance the cookie looks sufficient;
 *   - but refresh tokens are SINGLE-USE with rotation AND reuse-detection. Verified against the running
 *     backend: replaying one refresh cookie returns 200 the first time and 401 the second, and a detected
 *     reuse revokes the whole descendant token family (AuthService.cs:480-483).
 * So thirty browser contexts restoring from one saved state would give one pass and twenty-nine 401s — worse
 * than logging in each time, and failing in a way that looks like an application bug. If the per-test login
 * cost genuinely needs removing, the shape that works is a worker-scoped shared browser CONTEXT (one login,
 * one silent restore, access token alive in memory for the run) — at the cost of order-dependence between
 * tests, which is its own flakiness class. Not done here; measure first.
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
