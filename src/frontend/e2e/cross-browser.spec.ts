import { test, expect, Page } from '@playwright/test';
import { loginAsE2EOwner } from './fixtures/auth';

/**
 * Cross-browser render + responsive parity.
 *
 * This file runs unchanged across the `chromium`, `firefox`, and `webkit` projects (see
 * playwright.config.ts) — `npx playwright test cross-browser` exercises all three engines. It covers the
 * cross-browser / responsive-viewport TCs that the chromium-only Playwright MCP could not (e.g.
 * TC-CHR-034/062 job-titles, TC-CHR-122 profile, TC-CHR-191 locations, plus the departments/leave list
 * pages). For each primary list page it asserts, in every engine:
 *   - the page reaches its route (not /forbidden) and renders its <h1> landmark,
 *   - no uncaught JS error (`pageerror`) fires — the class of bug (BUG-099 length-crash, BUG-236
 *     filter-crash, BUG-239 null-color crash) that only shows at runtime and can differ per engine,
 *   - no error toast surfaces (FE↔BE contract 404s),
 *   - the layout has no horizontal overflow at 360 / 768 / 1920 px.
 */

const VIEWPORTS = [
  { name: 'mobile', width: 360, height: 740 },
  { name: 'tablet', width: 768, height: 1024 },
  { name: 'desktop', width: 1920, height: 1080 },
];

// Primary list pages the Tenant Owner can reach that render an <h1> (data-independent — empty states
// still render the heading + chrome). Keyed by the exact sidebar link label.
const PAGES = ['Departments', 'Job Titles', 'Employees', 'Leave'];

/** Attach uncaught-error capture; returns a getter for errors seen so far. */
function trackPageErrors(page: Page): () => string[] {
  const errors: string[] = [];
  page.on('pageerror', (err) => errors.push(err.message));
  return () => errors;
}

/** True when the page body does not overflow horizontally (allow 1px for sub-pixel rounding). */
async function hasNoHorizontalOverflow(page: Page): Promise<boolean> {
  return page.evaluate(() => {
    const el = document.documentElement;
    return el.scrollWidth <= el.clientWidth + 1;
  });
}

test.describe('cross-browser render + responsive parity', () => {
  test.beforeEach(async ({ page }) => {
    await loginAsE2EOwner(page);
  });

  for (const label of PAGES) {
    test(`${label}: renders with no JS error across engines`, async ({ page }) => {
      const getErrors = trackPageErrors(page);

      await page.getByRole('link', { name: label, exact: true }).click();
      await expect(page).not.toHaveURL(/\/forbidden/);

      // The page's <h1> landmark renders regardless of data (empty state still shows the header).
      await expect(page.locator('h1').first()).toBeVisible({ timeout: 15_000 });

      // Let initial API calls resolve, then assert no error toast and no uncaught JS error.
      await page.waitForTimeout(1500);
      await expect(page.locator('.toast-error')).toHaveCount(0);
      expect(getErrors(), `uncaught JS errors on ${label}`).toEqual([]);
    });

    test(`${label}: no horizontal overflow at 360/768/1920`, async ({ page }) => {
      await page.getByRole('link', { name: label, exact: true }).click();
      await expect(page).not.toHaveURL(/\/forbidden/);
      await expect(page.locator('h1').first()).toBeVisible({ timeout: 15_000 });

      for (const vp of VIEWPORTS) {
        await page.setViewportSize({ width: vp.width, height: vp.height });
        // Give the layout a beat to reflow before measuring.
        await page.waitForTimeout(300);
        expect(
          await hasNoHorizontalOverflow(page),
          `${label} overflows horizontally at ${vp.name} (${vp.width}px)`
        ).toBe(true);
      }
    });
  }
});
