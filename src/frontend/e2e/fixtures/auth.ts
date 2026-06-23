import { Page } from '@playwright/test';

/**
 * Shared E2E credentials for the dev-only `e2e` business tenant (seeded by DbInitializer in Development).
 * owner@e2e.test is a "Tenant Owner", so it has full HR access.
 */
export const E2E_TENANT = 'e2e';
export const E2E_OWNER_EMAIL = 'owner@e2e.test';
export const E2E_OWNER_PASSWORD = 'E2ePass@123!';

/**
 * Logs in to the `e2e` tenant as the Tenant Owner via the real password login form, then waits until the
 * SPA is authenticated. Dev tenant selection is via the `?tenant=` query param (TenantService persists it).
 */
export async function loginAsE2EOwner(page: Page): Promise<void> {
  // Land on the login page for the e2e tenant. The query param sets the active tenant for dev.
  await page.goto(`/?tenant=${E2E_TENANT}`);

  // Wait for the login FORM (robust against SPA pushState navigation that doesn't fire a 'load' event,
  // which makes waitForURL flaky). The app redirects unauthenticated users to /auth/login.
  const email = page.getByTestId('login-email');
  await email.waitFor({ state: 'visible', timeout: 30_000 });

  await email.fill(E2E_OWNER_EMAIL);
  await page.getByTestId('login-password').fill(E2E_OWNER_PASSWORD);
  await page.getByTestId('login-submit').click();

  // Authenticated: the main sidebar (with the Dashboard nav link) renders once logged in.
  await page
    .getByRole('link', { name: 'Dashboard' })
    .waitFor({ state: 'visible', timeout: 30_000 });
}
