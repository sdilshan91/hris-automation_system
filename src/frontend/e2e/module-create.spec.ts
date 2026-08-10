import { test, expect } from '@playwright/test';
import { loginAsE2EOwner } from './fixtures/auth';

/**
 * Deeper module flows beyond the Core HR department create:
 *  - A real Job Titles create → appears in the list.
 *  - #2 verification: the employee-linked owner can open Leave + Attendance WITHOUT the fail-closed 403
 *    ("No employee record is linked") that an unlinked user hits — proving the e2e employee-persona seed.
 */

test.beforeEach(async ({ page }) => {
  await loginAsE2EOwner(page);
});

test('job-titles: create and see in list', async ({ page }) => {
  await page.getByRole('link', { name: 'Job Titles' }).click();
  await expect(page).not.toHaveURL(/\/forbidden/);

  const name = `E2E Title ${Date.now()}`;
  await page.getByRole('button', { name: /add job title/i }).first().click();
  await page.getByPlaceholder('e.g. Software Engineer').fill(name);
  await page.getByRole('button', { name: 'Create Job Title' }).click();

  await expect(page.getByText(name).first()).toBeVisible({ timeout: 15_000 });
});

test('#2 leave: opens for the employee-linked owner (no fail-closed 403)', async ({ page }) => {
  await page.getByRole('link', { name: 'Leave', exact: true }).click();
  await expect(page).not.toHaveURL(/\/forbidden/);
  // The employee-persona seed means leave self-service must not error with "no employee record".
  await page.waitForTimeout(1500);
  await expect(page.locator('.toast-error')).toHaveCount(0);
});

test('#2 attendance: opens for the employee-linked owner (no fail-closed 403)', async ({ page }) => {
  await page.getByRole('link', { name: 'Attendance Dashboard', exact: true }).click();
  await expect(page).not.toHaveURL(/\/forbidden/);
  await page.waitForTimeout(1500);
  await expect(page.locator('.toast-error')).toHaveCount(0);
});
