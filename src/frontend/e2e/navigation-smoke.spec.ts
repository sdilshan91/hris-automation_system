import { test, expect } from '@playwright/test';
import { loginAsE2EOwner } from './fixtures/auth';

/**
 * Navigation smoke — logs in once as the Tenant Owner and visits every primary module page via the
 * sidebar (client-side routing). For each it asserts the route is reachable (not /forbidden) and the page
 * loads without an error toast. This is the cheap, broad net for the FE↔BE contract bugs we keep hitting:
 * a wrong service URL 404s → the error interceptor raises a `.toast-error`; a missing role on a route guard
 * → /forbidden. One failing entry pinpoints the module.
 */

// Sidebar labels a Tenant Owner sees (platform-only items like Tenants/Monitoring/Plans are excluded).
const MODULES = [
  'Departments',
  'Job Titles',
  'Employees',
  'Leave',
  'Attendance',
  'Payroll',
  'Recruitment',
  'Performance',
  'Reports',
  'Onboarding',
  'Users',
  'Roles',
  'Settings',
  'Workflows',
  'Audit Log',
  'Data Export',
];

test.beforeEach(async ({ page }) => {
  await loginAsE2EOwner(page);
});

for (const label of MODULES) {
  test(`nav: ${label} loads without error`, async ({ page }) => {
    await page.getByRole('link', { name: label, exact: true }).click();

    // Reachable (route guard passed).
    await expect(page).not.toHaveURL(/\/forbidden/);

    // Give the page's initial API calls a moment to resolve, then assert no error toast surfaced
    // (a wrong URL / failed load triggers ngx-toastr's `.toast-error`).
    await page.waitForTimeout(1500);
    await expect(page.locator('.toast-error')).toHaveCount(0);
  });
}
