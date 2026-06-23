import { test, expect } from '@playwright/test';
import { loginAsE2EOwner } from './fixtures/auth';

/**
 * Core HR smoke tests — drive the real UI against the real API.
 *
 * Their PRIMARY value is catching FE↔BE contract drift: a wrong service URL 404s, a missing form field
 * fails validation, a route guard sends the user to /forbidden.
 *
 * IMPORTANT: the SPA keeps the access token IN MEMORY only, so a full-page navigation (page.goto) reloads
 * and logs out. After login we navigate via the sidebar links (client-side routing), never page.goto.
 */

test.beforeEach(async ({ page }) => {
  await loginAsE2EOwner(page);
});

test('departments: create with code and see it in the list', async ({ page }) => {
  await page.getByRole('link', { name: 'Departments' }).click();
  await expect(page).toHaveURL(/\/departments/);
  await expect(page).not.toHaveURL(/\/forbidden/);

  const unique = `E2E Dept ${Date.now()}`;
  const code = `E2E-${Date.now().toString().slice(-6)}`;

  await page.getByRole('button', { name: /add department/i }).first().click();
  await page.getByPlaceholder('e.g. Engineering').fill(unique);
  await page.getByPlaceholder('e.g. ENG-01').fill(code);
  await page.getByTestId('department-submit').click();

  // Assert the new department card appears (its aria-label is "Edit department: <name>"), scoped tightly
  // so it does not also match the transient success toast.
  await expect(
    page.getByRole('button', { name: `Edit department: ${unique}` })
  ).toBeVisible({ timeout: 15_000 });
});

test('job-titles: page loads without error', async ({ page }) => {
  await page.getByRole('link', { name: 'Job Titles' }).click();
  await expect(page).toHaveURL(/\/job-titles/);
  await expect(page).not.toHaveURL(/\/forbidden/);
  await expect(page.locator('.toast-error')).toHaveCount(0);
});

test('employees: page loads without error', async ({ page }) => {
  await page.getByRole('link', { name: 'Employees' }).click();
  await expect(page).toHaveURL(/\/employees/);
  await expect(page).not.toHaveURL(/\/forbidden/);
  await expect(page.locator('.toast-error')).toHaveCount(0);
});
