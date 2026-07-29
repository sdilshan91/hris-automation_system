import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { TenantService } from './tenant.service';

/**
 * Canonical module keys for plan-based module entitlement (US-ADM-012 AC-2).
 *
 * MUST stay in sync with:
 *   - CANONICAL_MODULES in features/admin/plans/models/plan.models.ts, and
 *   - the backend PlanModules canonical set.
 * It is duplicated here (rather than imported from the plans feature) so that
 * core/ never depends on a feature module — a core→feature import would invert
 * the dependency direction the app deliberately maintains.
 *
 * `CoreHR` is always-on and must never be gated.
 */
export const CANONICAL_MODULE_KEYS: readonly string[] = [
  'CoreHR',
  'Leave',
  'Attendance',
  'Recruitment',
  'Onboarding',
  'Payroll',
  'Performance',
  'Training',
  'Asset',
  'Benefits',
  'Reporting',
  'CustomReportBuilder',
  'PublicCareersPage',
];

const CANONICAL_SET = new Set<string>(CANONICAL_MODULE_KEYS);

/**
 * Whether `module` is entitled for the tenant, given its `enabledModules` list.
 *
 * This MUST mirror the backend predicate `PlanModules.IsModuleEnabled` exactly — if
 * the FE and BE disagree, the UI and the API will disagree about what is enabled
 * (a link shows but its API 403s, or vice-versa).
 *
 * FAIL OPEN. `enabled_modules` was historically seeded with *permission prefixes*
 * (`Audit`, `CustomField`, `Roles`, `Department`, `Employee`, `Tenant`, `Reports`…)
 * instead of canonical module keys — see ISSUE-335. That legacy vocabulary OVERLAPS
 * the canonical one (`Leave`, `Payroll`, `Attendance` are valid tokens in BOTH), so a
 * naive "is the module in the list?" check would treat those legacy lists as
 * authoritative and wrongly lock the tenant out of everything (including CoreHR).
 *
 * The rule, therefore:
 *   1. null/empty list           → fully entitled (allow) — nothing configured yet.
 *   2. ANY non-canonical token   → fully entitled (allow) — legacy/unknown vocabulary,
 *                                   the list is not a real module-entitlement list.
 *   3. every token is canonical  → the list IS authoritative → allow iff it contains
 *                                   `module`.
 */
export function isModuleEntitled(
  module: string,
  enabledModules: readonly string[] | null | undefined
): boolean {
  // (1) Nothing configured — treat as fully entitled.
  if (!enabledModules || enabledModules.length === 0) {
    return true;
  }

  // (2) Legacy/unknown vocabulary — any token that is not a canonical module key
  // means this list is NOT an authoritative module-entitlement list, so fail open.
  const everyTokenIsCanonical = enabledModules.every((token) =>
    CANONICAL_SET.has(token)
  );
  if (!everyTokenIsCanonical) {
    return true;
  }

  // (3) Authoritative list of canonical keys.
  return enabledModules.includes(module);
}

/**
 * Route guard that blocks a feature route when its module is not entitled by the
 * tenant's current plan (US-ADM-012 AC-2). Mirrors permissionGuard/roleGuard in
 * core/auth/auth.guard.ts — redirects to /forbidden when not entitled.
 *
 * Usage: `canActivate: [moduleGuard('Payroll'), roleGuard([...])]`
 */
export function moduleGuard(module: string): CanActivateFn {
  return () => {
    const tenantService = inject(TenantService);
    const router = inject(Router);

    if (isModuleEntitled(module, tenantService.enabledModules())) {
      return true;
    }

    return router.createUrlTree(['/forbidden']);
  };
}
