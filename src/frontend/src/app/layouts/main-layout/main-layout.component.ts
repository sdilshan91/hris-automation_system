import {
  Component,
  ChangeDetectionStrategy,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { EMPTY } from 'rxjs';
import { catchError, finalize } from 'rxjs/operators';
import { AuthService } from '../../core/auth/auth.service';
import { SYSTEM_ROLES } from '../../core/auth/auth.guard';
import { IUserTenant } from '../../core/auth/auth.models';
import { TenantService } from '../../core/tenant/tenant.service';
import { isModuleEntitled } from '../../core/tenant/module.guard';
import { IdleTimeoutService } from '../../core/services/idle-timeout.service';
import { IdleTimeoutWarningComponent } from '../../shared/components/idle-timeout-warning/idle-timeout-warning.component';
import { ImpersonationBannerComponent } from '../../features/admin/impersonation/components/impersonation-banner/impersonation-banner.component';
import { NotificationBellComponent } from '../../features/notifications/components/notification-bell/notification-bell.component';
import { LogoFallbackDirective } from '../../shared/directives/logo-fallback.directive';

export interface INavItem {
  label: string;
  icon: string;
  route: string;
  /**
   * Tenant permission gate. A single string requires that exact permission; an
   * array is an "any-of" gate (item shows if the user holds ANY listed permission).
   * The any-of form is needed where a route admits several roles that hold
   * different permissions (e.g. /performance: Manager has Performance.View.Team
   * while HR/Admin have Performance.View.All) — see ISSUE-210.
   */
  permission?: string | string[];
  /**
   * US-ADM-009 / BUG-493: System-Admin-console items gate on PLATFORM roles, not on a tenant
   * permission, and the presence of this field is what marks an item as a system-console item in
   * the persona split below. It is a LIST because /admin/monitoring is roleGuard(['SystemAdmin',
   * 'System Support']): the earlier single-role form hid that page from System Support while
   * showing them the whole tenant menu, none of whose routes their roles can enter.
   */
  systemRoles?: string[];
  /**
   * ISSUE-214: "any-of" TENANT-role gate, for routes whose `canActivate` is a
   * `roleGuard([...])` rather than a permission check (e.g. notification templates,
   * guarded on Tenant Admin / Tenant Owner). Gating those on a permission proxy would
   * let nav visibility drift from route access — the ISSUE-210 defect. Distinct from
   * `role`, which selects the System-Admin persona split.
   */
  tenantRoles?: string[];
  /**
   * US-ADM-012 AC-2: canonical module key this item belongs to (e.g. 'Payroll').
   * When set, the item is hidden if the tenant's plan does not entitle that module
   * (see isModuleEntitled — fails OPEN for legacy/unknown module lists). Untagged
   * items are never module-gated: CoreHR is always-on and platform/admin surfaces
   * must never be gated.
   */
  module?: string;
}

/**
 * The sidebar table. Exported at module scope (BUG-493) so main-layout.nav-gate-invariant.spec.ts
 * can read every item's gate and compare it against the guards on the route it links to, without
 * instantiating the shell. Each item is gated with the SAME mechanism its route uses:
 *
 *   `tenantRoles`  ← the route's roleGuard(...)        (compare with roleGuard's effectiveRoles)
 *   `permission`   ← the route's permissionGuard([...]) (any-of)
 *   `module`       ← the route's moduleGuard('Key')
 *   `systemRoles`  ← a platform roleGuard, e.g. roleGuard(['SystemAdmin'])
 *
 * A gate that PROXIES the route (a permission key standing in for a roleGuard) is the BUG-493
 * defect: two systems answering "may this persona use this feature?", free to drift apart in
 * either direction — a link that dead-ends at /forbidden, or a working page nobody can find.
 * The invariant spec fails on any such proxy, so do not add one.
 */
export const NAV_ITEMS: INavItem[] = [
  {
    label: 'Dashboard',
    route: '/dashboard',
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M10.707 2.293a1 1 0 0 0-1.414 0l-7 7a1 1 0 0 0 1.414 1.414L4 10.414V17a1 1 0 0 0 1 1h2a1 1 0 0 0 1-1v-2a1 1 0 0 1 1-1h2a1 1 0 0 1 1 1v2a1 1 0 0 0 1 1h2a1 1 0 0 0 1-1v-6.586l.293.293a1 1 0 0 0 1.414-1.414l-7-7Z"/></svg>`,
  },
  {
    // BUG-450: this item shipped with NO gate at all. The nav filter treats a missing
    // `permission` as "visible to everyone" (`if (!item.permission) return true`), so every
    // Employee and Manager in every tenant was shown a prominent link that dead-ends at
    // /forbidden — the route is `roleGuard(['Tenant Admin', 'HR Officer'])`.
    // `tenantRoles` (not a permission proxy) for the same reason Locations documents below:
    // the route's guard IS a roleGuard, and gating nav on a permission key would let
    // visibility drift from access (the ISSUE-210 defect). 'Tenant Owner' is listed
    // explicitly because roleGuard implicitly widens every tenant guard with
    // TENANT_SUPER_ROLES (auth.guard.ts:63,76) — omit it and the link hides from a persona
    // that can actually enter.
    label: 'Departments',
    route: '/departments',
    tenantRoles: ['Tenant Admin', 'HR Officer', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M4.25 2A2.25 2.25 0 0 0 2 4.25v2.5A2.25 2.25 0 0 0 4.25 9h2.5A2.25 2.25 0 0 0 9 6.75v-2.5A2.25 2.25 0 0 0 6.75 2h-2.5Zm0 9A2.25 2.25 0 0 0 2 13.25v2.5A2.25 2.25 0 0 0 4.25 18h2.5A2.25 2.25 0 0 0 9 15.75v-2.5A2.25 2.25 0 0 0 6.75 11h-2.5Zm9-9A2.25 2.25 0 0 0 11 4.25v2.5A2.25 2.25 0 0 0 13.25 9h2.5A2.25 2.25 0 0 0 18 6.75v-2.5A2.25 2.25 0 0 0 15.75 2h-2.5Zm0 9A2.25 2.25 0 0 0 11 13.25v2.5A2.25 2.25 0 0 0 13.25 18h2.5A2.25 2.25 0 0 0 18 15.75v-2.5A2.25 2.25 0 0 0 15.75 11h-2.5Z" clip-rule="evenodd"/></svg>`,
  },
  {
    // BUG-450: this item shipped with NO gate at all. The nav filter treats a missing
    // `permission` as "visible to everyone" (`if (!item.permission) return true`), so every
    // Employee and Manager in every tenant was shown a prominent link that dead-ends at
    // /forbidden — the route is `roleGuard(['Tenant Admin', 'HR Officer'])`.
    // `tenantRoles` (not a permission proxy) for the same reason Locations documents below:
    // the route's guard IS a roleGuard, and gating nav on a permission key would let
    // visibility drift from access (the ISSUE-210 defect). 'Tenant Owner' is listed
    // explicitly because roleGuard implicitly widens every tenant guard with
    // TENANT_SUPER_ROLES (auth.guard.ts:63,76) — omit it and the link hides from a persona
    // that can actually enter.
    label: 'Job Titles',
    route: '/job-titles',
    tenantRoles: ['Tenant Admin', 'HR Officer', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M6 3.75A2.75 2.75 0 0 1 8.75 1h2.5A2.75 2.75 0 0 1 14 3.75v.443c.572.055 1.14.122 1.706.2C17.053 4.582 18 5.75 18 7.07v3.469c0 1.126-.694 2.191-1.83 2.54-1.952.599-4.024.921-6.17.921s-4.219-.322-6.17-.921C2.694 12.73 2 11.665 2 10.539V7.07c0-1.321.947-2.489 2.294-2.676A41.047 41.047 0 0 1 6 4.193V3.75Zm6.5 0v.325a41.622 41.622 0 0 0-5 0V3.75c0-.69.56-1.25 1.25-1.25h2.5c.69 0 1.25.56 1.25 1.25ZM10 10a1 1 0 0 0-1 1v.01a1 1 0 0 0 1 1h.01a1 1 0 0 0 1-1V11a1 1 0 0 0-1-1H10Z" clip-rule="evenodd"/><path d="M3 15.055v-.684c.126.053.255.1.39.142 2.092.642 4.313.987 6.61.987 2.297 0 4.518-.345 6.61-.987.135-.041.264-.089.39-.142v.684c0 1.347-.985 2.53-2.363 2.686a41.454 41.454 0 0 1-9.274 0C3.985 17.585 3 16.402 3 15.055Z"/></svg>`,
  },
  {
    // US-CHR-007 / ISSUE-372 (E5): the Locations master-data page shipped reachable by
    // URL only — no nav entry anywhere — so a built feature was invisible. Gated with
    // `tenantRoles` (not a permission proxy) because the route's guard IS a roleGuard and
    // the permission catalog carries no Location.* key; a proxy would let nav visibility
    // drift from route access (the ISSUE-210 defect). 'Tenant Owner' is listed explicitly
    // because roleGuard implicitly widens every tenant guard with it (auth.guard.ts
    // TENANT_SUPER_ROLES) — omitting it would hide the link from a persona that can enter.
    label: 'Locations',
    route: '/locations',
    tenantRoles: ['Tenant Admin', 'HR Officer', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M9.69 18.933c.003.001.006.003.31-.933l-.31.933a.75.75 0 0 0 .62 0l-.31-.933c.304.936.307.934.31.933l.005-.002.014-.005a5.7 5.7 0 0 0 .22-.09 12.1 12.1 0 0 0 2.412-1.407C14.583 16.128 17 13.417 17 9A7 7 0 1 0 3 9c0 4.417 2.417 7.128 4.03 8.43a12.1 12.1 0 0 0 2.411 1.406 5.7 5.7 0 0 0 .22.09l.014.005.005.002ZM10 11.25a2.25 2.25 0 1 0 0-4.5 2.25 2.25 0 0 0 0 4.5Z" clip-rule="evenodd"/></svg>`,
  },
  {
    label: 'Salary Grades',
    route: '/salary-grades',
    tenantRoles: ['HR Officer', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M1 4.25C1 3.56 1.56 3 2.25 3h15.5c.69 0 1.25.56 1.25 1.25v8.5c0 .69-.56 1.25-1.25 1.25H2.25C1.56 14 1 13.44 1 12.75v-8.5ZM3.5 5.5a1 1 0 0 1-1 1V6a1 1 0 0 1 1-1h.01a1 1 0 0 1-.01 1Zm13 5a1 1 0 0 1 1-1v.5a1 1 0 0 1-1 1h-.01a1 1 0 0 1 .01-1ZM10 6.5A2.5 2.5 0 1 0 10 11.5a2.5 2.5 0 0 0 0-5Z"/><path d="M2.25 16a.75.75 0 0 0 0 1.5h15.5a.75.75 0 0 0 0-1.5H2.25Z"/></svg>`,
  },
  {
    label: 'Employees',
    route: '/employees',
    tenantRoles: ['HR Officer', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M7 8a3 3 0 1 0 0-6 3 3 0 0 0 0 6Zm7.5 1a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5ZM1.615 16.428a1.224 1.224 0 0 1-.569-1.175 6.002 6.002 0 0 1 11.908 0c.058.467-.172.92-.57 1.174A9.953 9.953 0 0 1 7 18a9.953 9.953 0 0 1-5.385-1.572ZM14.5 16h-.106c.07-.297.088-.611.048-.933a7.47 7.47 0 0 0-1.588-3.755 4.502 4.502 0 0 1 5.874 2.636.818.818 0 0 1-.36.98A7.465 7.465 0 0 1 14.5 16Z"/></svg>`,
  },
  {
    // US-CHR-006 / ISSUE-372 (E5): the Organization Tree shipped URL-only. Same
    // `tenantRoles` reasoning as Locations above; this route's guard admits Manager too,
    // so the nav gate must list Manager or a manager who can enter sees no link.
    label: 'Org Tree',
    route: '/org-tree',
    tenantRoles: ['Tenant Admin', 'HR Officer', 'Manager', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M8 2.5A1.5 1.5 0 0 1 9.5 1h1A1.5 1.5 0 0 1 12 2.5v1A1.5 1.5 0 0 1 10.5 5h-.25v2.75h3.5A1.75 1.75 0 0 1 15.5 9.5v1.25h.25a1.5 1.5 0 0 1 1.5 1.5v1a1.5 1.5 0 0 1-1.5 1.5h-1a1.5 1.5 0 0 1-1.5-1.5v-1a1.5 1.5 0 0 1 1.5-1.5H14V9.5a.25.25 0 0 0-.25-.25h-3.5v1.5h.25a1.5 1.5 0 0 1 1.5 1.5v1a1.5 1.5 0 0 1-1.5 1.5h-1a1.5 1.5 0 0 1-1.5-1.5v-1a1.5 1.5 0 0 1 1.5-1.5h.25v-1.5h-3.5a.25.25 0 0 0-.25.25v1.25h.25a1.5 1.5 0 0 1 1.5 1.5v1a1.5 1.5 0 0 1-1.5 1.5h-1a1.5 1.5 0 0 1-1.5-1.5v-1a1.5 1.5 0 0 1 1.5-1.5h.25V9.5a1.75 1.75 0 0 1 1.75-1.75h3.5V5H9.5A1.5 1.5 0 0 1 8 3.5v-1Z"/></svg>`,
  },
  {
    label: 'Leave',
    route: '/leave',
    module: 'Leave',
    tenantRoles: ['Employee', 'HR Officer', 'Manager', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M5.75 2a.75.75 0 0 1 .75.75V4h7V2.75a.75.75 0 0 1 1.5 0V4h.25A2.75 2.75 0 0 1 18 6.75v8.5A2.75 2.75 0 0 1 15.25 18H4.75A2.75 2.75 0 0 1 2 15.25v-8.5A2.75 2.75 0 0 1 4.75 4H5V2.75A.75.75 0 0 1 5.75 2Zm-1 5.5c-.69 0-1.25.56-1.25 1.25v6.5c0 .69.56 1.25 1.25 1.25h10.5c.69 0 1.25-.56 1.25-1.25v-6.5c0-.69-.56-1.25-1.25-1.25H4.75Z" clip-rule="evenodd"/></svg>`,
  },
  // LOP Management — same discoverability defect as the DEC-D5 block below and as
  // ISSUE-208 (Attendance) / ISSUE-210 (Performance): the screen at '/leave/lop' has been
  // routed and role-guarded all along, but NOTHING linked to it — no sidebar entry, no
  // routerLink, no card. It was reachable only by typing the URL.
  //
  // Surfaced by @integration-enforcer while auditing the B2 register fix: repairing that
  // screen's data load would otherwise have delivered nothing, because no user could get
  // to the screen to see it work. A fix behind an unreachable route is not a fix.
  //
  // Placed with the day-to-day Leave item rather than in the config block: assigning LOP and
  // running a company shutdown are HR ACTIONS, not configuration.
  //
  // BUG-493: previously gated on the Leave.ManageLop permission — a PROXY for the route's real
  // gate, which is moduleGuard('Leave') + roleGuard(['Employee','Manager','HR Officer',
  // 'Tenant Admin']) INTERSECTED with the child roleGuard(['HR Officer','Tenant Admin']). The
  // proxy hid the link from a Tenant Owner, who passes every tenant roleGuard.
  {
    label: 'LOP Management',
    route: '/leave/lop',
    module: 'Leave',
    tenantRoles: ['HR Officer', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M10 18a8 8 0 1 0 0-16 8 8 0 0 0 0 16Zm.75-11.25a.75.75 0 0 0-1.5 0v3.5a.75.75 0 0 0 .3.6l2.25 1.75a.75.75 0 1 0 .9-1.2l-1.95-1.5V6.75Z" clip-rule="evenodd" /></svg>`,
  },
  // ─── Leave configuration (DEC-D5) ───────────────────────────────────────
  // Four HR/admin leave-config screens under the '/leave-types' tree were
  // reachable ONLY by direct URL — no sidebar entry existed (the Finance-facing
  // accrual-over-credit review surfaced the gap, but it covered all four). Same
  // discoverability defect class as ISSUE-208 (Attendance) / ISSUE-210 (Performance).
  //
  // Kept as their own block (NOT mixed with the employee '/leave' item above) so an
  // HR officer scanning for day-to-day leave actions doesn't trip over config tools.
  //
  // Gating mirrors ROUTE ACCESS exactly: the '/leave-types' parent guards on
  // moduleGuard('Leave') + roleGuard(['Tenant Admin','HR Manager','HR Officer']), so these
  // items carry that role list (plus 'Tenant Owner', which roleGuard adds implicitly) and
  // module:'Leave'.
  //
  // BUG-493: they previously gated on the Leave.ConfigurePolicy permission instead. That proxy
  // held only as long as the catalog and the route's role list agreed — and they had ALREADY
  // disagreed once: HR Manager holds Leave.ConfigurePolicy, so it saw these links and was
  // bounced to /forbidden until the route's list was corrected on 2026-08-02 (see that guard's
  // own comment in app.routes.ts). Mirroring the roleGuard removes the class of drift instead
  // of re-synchronising two lists after each divergence.
  {
    label: 'Entitlement Rules',
    route: '/leave-types/entitlements',
    module: 'Leave',
    tenantRoles: ['HR Manager', 'HR Officer', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M7.84 1.804A1 1 0 0 1 8.82 1h2.36a1 1 0 0 1 .98.804l.331 1.652a6.993 6.993 0 0 1 1.929 1.115l1.598-.54a1 1 0 0 1 1.186.447l1.18 2.044a1 1 0 0 1-.205 1.251l-1.267 1.113a7.047 7.047 0 0 1 0 2.228l1.267 1.113a1 1 0 0 1 .206 1.25l-1.18 2.045a1 1 0 0 1-1.187.447l-1.598-.54a6.993 6.993 0 0 1-1.929 1.115l-.33 1.652a1 1 0 0 1-.98.804H8.82a1 1 0 0 1-.98-.804l-.331-1.652a6.993 6.993 0 0 1-1.929-1.115l-1.598.54a1 1 0 0 1-1.186-.447l-1.18-2.044a1 1 0 0 1 .205-1.251l1.267-1.114a7.05 7.05 0 0 1 0-2.227L1.821 7.773a1 1 0 0 1-.206-1.25l1.18-2.045a1 1 0 0 1 1.187-.447l1.598.54A6.993 6.993 0 0 1 7.51 3.456l.33-1.652ZM10 13a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z" clip-rule="evenodd"/></svg>`,
  },
  {
    label: 'Holiday Calendar',
    route: '/leave-types/holidays',
    module: 'Leave',
    tenantRoles: ['HR Manager', 'HR Officer', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M5.75 2a.75.75 0 0 1 .75.75V4h7V2.75a.75.75 0 0 1 1.5 0V4h.25A2.75 2.75 0 0 1 18 6.75v8.5A2.75 2.75 0 0 1 15.25 18H4.75A2.75 2.75 0 0 1 2 15.25v-8.5A2.75 2.75 0 0 1 4.75 4H5V2.75A.75.75 0 0 1 5.75 2Zm-1 5.5c-.69 0-1.25.56-1.25 1.25v6.5c0 .69.56 1.25 1.25 1.25h10.5c.69 0 1.25-.56 1.25-1.25v-6.5c0-.69-.56-1.25-1.25-1.25H4.75Z" clip-rule="evenodd"/></svg>`,
  },
  {
    label: 'Carry-Forward Preview',
    route: '/leave-types/carry-forward-preview',
    module: 'Leave',
    tenantRoles: ['HR Manager', 'HR Officer', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M15.5 2A1.5 1.5 0 0 1 17 3.5v13A1.5 1.5 0 0 1 15.5 18h-11A1.5 1.5 0 0 1 3 16.5v-13A1.5 1.5 0 0 1 4.5 2h11ZM6 13.25a.75.75 0 0 1 .75.75v.5a.75.75 0 0 1-1.5 0v-.5a.75.75 0 0 1 .75-.75Zm3-3a.75.75 0 0 1 .75.75v3.5a.75.75 0 0 1-1.5 0V11a.75.75 0 0 1 .75-.75Zm3-2a.75.75 0 0 1 .75.75v5.5a.75.75 0 0 1-1.5 0V9a.75.75 0 0 1 .75-.75Z"/></svg>`,
  },
  {
    // DEC-D5: user-facing label deliberately describes the PURPOSE, not the
    // originating BUG-291 — Finance staff who have never heard of the bug id
    // must still understand what this screen is for.
    label: 'Accrual Over-Credit Review',
    route: '/leave-types/accrual-over-credit-exposure',
    module: 'Leave',
    tenantRoles: ['HR Manager', 'HR Officer', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.516 2.625H3.72c-1.347 0-2.189-1.458-1.515-2.625L8.485 2.495ZM10 5a.75.75 0 0 1 .75.75v3.5a.75.75 0 0 1-1.5 0v-3.5A.75.75 0 0 1 10 5Zm0 9a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clip-rule="evenodd"/></svg>`,
  },
  // ─── Attendance (US-ATT-001..010, ISSUE-208) ────────────────────────────
  // The module has ~13 child routes but only the clock-in landing was reachable
  // from the sidebar; every manager/HR sub-page (approvals, shifts, reports,
  // policy, reconciliation…) was orphaned — same discoverability defect as the
  // shipped ISSUE-210 (Performance). The nav renderer is FLAT (INavItem has no
  // children/grouping), so — matching how Payroll exposes its sub-pages as
  // sibling flat items — each attendance destination is a gated flat item.
  //
  // Gating mirrors ROUTE ACCESS (nav visibility must match what the route's guard admits — the
  // ISSUE-210 lesson). These routes guard by ROLE, and the parent '/attendance' guard admits
  // ONLY {Employee, Manager, HR Officer, Tenant Admin}, so each child's EFFECTIVE admitted set
  // is (parent INTERSECT child-guard) — which is what each `tenantRoles` below states. Note
  // HR Manager: several child guards list it, the parent does not, so it is admitted by NEITHER
  // and appears in no attendance nav gate.
  //
  // BUG-493: every one of these thirteen items used to gate on the catalog permission its
  // admitted roles happen to hold (Attendance.CheckIn, .Approve.Team, .Shift.Manage, …). That
  // is a PROXY for a role guard, and it drifted both ways — an Employee holding
  // Attendance.CheckIn saw "Clock In" only because the proxy agreed by coincidence, while a
  // Tenant Owner, admitted to all thirteen routes, was shown none of them. It also forced
  // per-item reasoning about which role holds which permission (the HR read items had to pick
  // Attendance.Edit over .View.All purely to stop an Auditor — blocked at the parent guard —
  // from getting an unreachable link). Mirroring the roleGuard makes all of that unnecessary.

  // Employee self-service ─ the base '/attendance' role set, which every self page inherits.
  {
    label: 'Clock In',
    route: '/attendance/clock-in',
    module: 'Attendance',
    tenantRoles: ['Employee', 'HR Officer', 'Manager', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M10 18a8 8 0 1 0 0-16 8 8 0 0 0 0 16Zm.75-13a.75.75 0 0 0-1.5 0v5c0 .414.336.75.75.75h4a.75.75 0 0 0 0-1.5h-3.25V5Z" clip-rule="evenodd"/></svg>`,
  },
  {
    label: 'My Regularization',
    route: '/attendance/regularization',
    module: 'Attendance',
    tenantRoles: ['Employee', 'HR Officer', 'Manager', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="m5.433 13.917 1.262-3.155A4 4 0 0 1 7.58 9.42l6.92-6.918a2.121 2.121 0 0 1 3 3l-6.92 6.918c-.383.383-.84.685-1.343.886l-3.154 1.262a.5.5 0 0 1-.65-.65Z"/><path d="M3.5 5.75c0-.69.56-1.25 1.25-1.25H10A.75.75 0 0 0 10 3H4.75A2.75 2.75 0 0 0 2 5.75v9.5A2.75 2.75 0 0 0 4.75 18h9.5A2.75 2.75 0 0 0 17 15.25V10a.75.75 0 0 0-1.5 0v5.25c0 .69-.56 1.25-1.25 1.25h-9.5c-.69 0-1.25-.56-1.25-1.25v-9.5Z"/></svg>`,
  },
  {
    label: 'My Overtime',
    route: '/attendance/overtime',
    module: 'Attendance',
    tenantRoles: ['Employee', 'HR Officer', 'Manager', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M10 1a9 9 0 1 0 0 18 9 9 0 0 0 0-18ZM8.94 6.94a.75.75 0 0 1 1.06 0l2.5 2.5a.75.75 0 0 1 0 1.06l-2.5 2.5a.75.75 0 1 1-1.06-1.06l1.22-1.22H6.75a.75.75 0 0 1 0-1.5h3.41L8.94 8a.75.75 0 0 1 0-1.06Z" clip-rule="evenodd"/></svg>`,
  },
  {
    label: 'My Lateness Score',
    route: '/attendance/lateness-score',
    module: 'Attendance',
    tenantRoles: ['Employee', 'HR Officer', 'Manager', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M9.664 1.319a.75.75 0 0 1 .672 0 41.059 41.059 0 0 1 8.198 5.424.75.75 0 0 1-.254 1.285 31.372 31.372 0 0 0-7.86 3.83.75.75 0 0 1-.84 0 31.508 31.508 0 0 0-2.08-1.287V9.394c0-.244.116-.463.302-.592a35.504 35.504 0 0 1 3.305-2.033.75.75 0 0 0-.714-1.319 37 37 0 0 0-3.446 2.12A2.216 2.216 0 0 0 6 9.393v.38a31.293 31.293 0 0 0-4.28-1.746.75.75 0 0 1-.254-1.285 41.059 41.059 0 0 1 8.198-5.424ZM6 11.459a29.848 29.848 0 0 0-2.455-1.158 41.029 41.029 0 0 0-.39 3.114.75.75 0 0 0 .419.74c.528.256 1.046.53 1.554.82-.21.324-.455.63-.739.914a.75.75 0 1 0 1.06 1.06c.37-.369.69-.77.96-1.193a26.61 26.61 0 0 1 3.095 2.348.75.75 0 0 0 .992 0 26.547 26.547 0 0 1 5.93-3.95.75.75 0 0 0 .42-.739 41.053 41.053 0 0 0-.39-3.114 29.925 29.925 0 0 0-5.199 2.801.75.75 0 0 1-.837 0A29.699 29.699 0 0 0 6 11.459Z" clip-rule="evenodd"/></svg>`,
  },
  // Manager / HR approver + monitoring.
  {
    label: 'Attendance Approvals',
    route: '/attendance/regularization-approvals',
    module: 'Attendance',
    tenantRoles: ['HR Officer', 'Manager', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M16.704 4.153a.75.75 0 0 1 .143 1.052l-8 10.5a.75.75 0 0 1-1.127.075l-4.5-4.5a.75.75 0 0 1 1.06-1.06l3.894 3.893 7.48-9.817a.75.75 0 0 1 1.05-.143Z" clip-rule="evenodd"/></svg>`,
  },
  {
    label: 'Overtime Approvals',
    route: '/attendance/overtime-approvals',
    module: 'Attendance',
    tenantRoles: ['HR Officer', 'Manager', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.516 2.625H3.72c-1.347 0-2.189-1.458-1.515-2.625L8.485 2.495ZM10 5a.75.75 0 0 1 .75.75v3.5a.75.75 0 0 1-1.5 0v-3.5A.75.75 0 0 1 10 5Zm0 9a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clip-rule="evenodd"/></svg>`,
  },
  {
    // The child guard adds HR Manager, which the parent excludes — so the intersection is
    // Manager + HR Officer + Tenant Admin, not the child's list.
    label: 'Attendance Dashboard',
    route: '/attendance/dashboard',
    module: 'Attendance',
    tenantRoles: ['HR Officer', 'Manager', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M15.5 2A1.5 1.5 0 0 1 17 3.5v13A1.5 1.5 0 0 1 15.5 18h-11A1.5 1.5 0 0 1 3 16.5v-13A1.5 1.5 0 0 1 4.5 2h11ZM6 13.25a.75.75 0 0 1 .75.75v.5a.75.75 0 0 1-1.5 0v-.5a.75.75 0 0 1 .75-.75Zm3-3a.75.75 0 0 1 .75.75v3.5a.75.75 0 0 1-1.5 0V11a.75.75 0 0 1 .75-.75Zm3-2a.75.75 0 0 1 .75.75v5.5a.75.75 0 0 1-1.5 0V9a.75.75 0 0 1 .75-.75Z"/></svg>`,
  },
  {
    label: 'Late / Early Report',
    route: '/attendance/late-early-report',
    module: 'Attendance',
    tenantRoles: ['HR Officer', 'Manager', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M10 18a8 8 0 1 0 0-16 8 8 0 0 0 0 16Zm.75-11.25a.75.75 0 0 0-1.5 0v3.19l-1.72 1.72a.75.75 0 1 0 1.06 1.06l1.94-1.94a.75.75 0 0 0 .22-.53V6.75Z" clip-rule="evenodd"/></svg>`,
  },
  // HR configuration & reporting ─ the child guards drop Manager, leaving HR Officer +
  // Tenant Admin (+ Tenant Owner).
  {
    label: 'Shifts',
    route: '/attendance/shifts',
    module: 'Attendance',
    tenantRoles: ['HR Officer', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M5.75 2a.75.75 0 0 1 .75.75V4h7V2.75a.75.75 0 0 1 1.5 0V4h.25A2.75 2.75 0 0 1 18 6.75v8.5A2.75 2.75 0 0 1 15.25 18H4.75A2.75 2.75 0 0 1 2 15.25v-8.5A2.75 2.75 0 0 1 4.75 4H5V2.75A.75.75 0 0 1 5.75 2Zm-1 5.5c-.69 0-1.25.56-1.25 1.25v6.5c0 .69.56 1.25 1.25 1.25h10.5c.69 0 1.25-.56 1.25-1.25v-6.5c0-.69-.56-1.25-1.25-1.25H4.75Z" clip-rule="evenodd"/></svg>`,
  },
  {
    label: 'Monthly Summary',
    route: '/attendance/monthly-summary',
    module: 'Attendance',
    tenantRoles: ['HR Officer', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M2 3.5A1.5 1.5 0 0 1 3.5 2h9A1.5 1.5 0 0 1 14 3.5v11.75A2.75 2.75 0 0 0 16.75 18h-12A2.75 2.75 0 0 1 2 15.25V3.5Zm3.75 7a.75.75 0 0 0 0 1.5h4.5a.75.75 0 0 0 0-1.5h-4.5Zm0 3a.75.75 0 0 0 0 1.5h4.5a.75.75 0 0 0 0-1.5h-4.5ZM5 5.75A.75.75 0 0 1 5.75 5h4.5a.75.75 0 0 1 .75.75v2.5a.75.75 0 0 1-.75.75h-4.5A.75.75 0 0 1 5 8.25v-2.5Z" clip-rule="evenodd"/><path d="M16.5 6.5h-1v8.75a1.25 1.25 0 1 0 2.5 0V8a1.5 1.5 0 0 0-1.5-1.5Z"/></svg>`,
  },
  {
    label: 'Overtime Report',
    route: '/attendance/overtime-report',
    module: 'Attendance',
    tenantRoles: ['HR Officer', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M15.5 2A1.5 1.5 0 0 1 17 3.5v13A1.5 1.5 0 0 1 15.5 18h-11A1.5 1.5 0 0 1 3 16.5v-13A1.5 1.5 0 0 1 4.5 2h11ZM6 13.25a.75.75 0 0 1 .75.75v.5a.75.75 0 0 1-1.5 0v-.5a.75.75 0 0 1 .75-.75Zm3-3a.75.75 0 0 1 .75.75v3.5a.75.75 0 0 1-1.5 0V11a.75.75 0 0 1 .75-.75Zm3-2a.75.75 0 0 1 .75.75v5.5a.75.75 0 0 1-1.5 0V9a.75.75 0 0 1 .75-.75Z"/></svg>`,
  },
  {
    label: 'Late Policy',
    route: '/attendance/late-policy',
    module: 'Attendance',
    tenantRoles: ['HR Officer', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M7.84 1.804A1 1 0 0 1 8.82 1h2.36a1 1 0 0 1 .98.804l.331 1.652a6.993 6.993 0 0 1 1.929 1.115l1.598-.54a1 1 0 0 1 1.186.447l1.18 2.044a1 1 0 0 1-.205 1.251l-1.267 1.113a7.047 7.047 0 0 1 0 2.228l1.267 1.113a1 1 0 0 1 .206 1.25l-1.18 2.045a1 1 0 0 1-1.187.447l-1.598-.54a6.993 6.993 0 0 1-1.929 1.115l-.33 1.652a1 1 0 0 1-.98.804H8.82a1 1 0 0 1-.98-.804l-.331-1.652a6.993 6.993 0 0 1-1.929-1.115l-1.598.54a1 1 0 0 1-1.186-.447l-1.18-2.044a1 1 0 0 1 .205-1.251l1.267-1.114a7.05 7.05 0 0 1 0-2.227L1.821 7.773a1 1 0 0 1-.206-1.25l1.18-2.045a1 1 0 0 1 1.187-.447l1.598.54A6.993 6.993 0 0 1 7.51 3.456l.33-1.652ZM10 13a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z" clip-rule="evenodd"/></svg>`,
  },
  {
    // US-ATT-009: attendance period lock + reconciliation before a payroll run.
    label: 'Payroll Integration',
    route: '/attendance/payroll-integration',
    module: 'Attendance',
    tenantRoles: ['HR Officer', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M10 1a4.5 4.5 0 0 0-4.5 4.5V9H5a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-6a2 2 0 0 0-2-2h-.5V5.5A4.5 4.5 0 0 0 10 1Zm3 8V5.5a3 3 0 1 0-6 0V9h6Z" clip-rule="evenodd"/></svg>`,
  },
  {
    label: 'Payroll',
    route: '/payroll',
    module: 'Payroll',
    tenantRoles: ['HR Officer', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M10.75 10.818v2.614A3.13 3.13 0 0 0 11.888 13c.482-.315.612-.648.612-.875 0-.227-.13-.56-.612-.875a3.13 3.13 0 0 0-1.138-.432ZM8.33 8.62c.053.055.115.11.184.164.208.16.46.284.736.363V6.603a2.45 2.45 0 0 0-.35.13c-.14.065-.27.143-.386.233-.377.292-.514.627-.514.909 0 .184.058.39.202.592.037.051.08.102.128.152Z"/><path fill-rule="evenodd" d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-8-6a.75.75 0 0 1 .75.75v.316a3.78 3.78 0 0 1 1.653.713c.426.33.744.74.925 1.2a.75.75 0 0 1-1.395.55 1.35 1.35 0 0 0-.447-.563 2.187 2.187 0 0 0-.736-.363V9.3c.698.093 1.383.32 1.959.696.787.514 1.29 1.27 1.29 2.13 0 .86-.504 1.616-1.29 2.13-.576.377-1.261.603-1.96.696v.299a.75.75 0 1 1-1.5 0v-.3a3.78 3.78 0 0 1-1.653-.712 3.22 3.22 0 0 1-.925-1.2.75.75 0 0 1 1.395-.55c.12.3.3.54.447.563a2.19 2.19 0 0 0 .736.363V10.7a5.007 5.007 0 0 1-1.96-.696C4.504 9.49 4 8.735 4 7.875c0-.86.504-1.616 1.29-2.13.577-.377 1.262-.603 1.96-.696V4.75A.75.75 0 0 1 10 4Z" clip-rule="evenodd"/></svg>`,
  },
  {
    // US-PAY-010 (FR-7, §8): pre-payroll reconciliation — attendance/leave summary per
    // employee + leave-encashment trigger. It carries no guard of its own, so it inherits the
    // '/payroll' parent's role set.
    label: 'Reconciliation',
    route: '/payroll/reconciliation',
    module: 'Payroll',
    tenantRoles: ['HR Officer', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M2 4.25A2.25 2.25 0 0 1 4.25 2h11.5A2.25 2.25 0 0 1 18 4.25v11.5A2.25 2.25 0 0 1 15.75 18H4.25A2.25 2.25 0 0 1 2 15.75V4.25Zm4.03 1.97a.75.75 0 0 0-1.06 1.06l1.5 1.5a.75.75 0 0 0 1.06 0l3-3a.75.75 0 1 0-1.06-1.06L7 7.19l-.97-.97ZM11.25 6.5a.75.75 0 0 0 0 1.5h3.5a.75.75 0 0 0 0-1.5h-3.5Zm0 5a.75.75 0 0 0 0 1.5h3.5a.75.75 0 0 0 0-1.5h-3.5Zm-6.5 0a.75.75 0 0 0 0 1.5h3.5a.75.75 0 0 0 0-1.5h-3.5Z" clip-rule="evenodd"/></svg>`,
  },
  {
    // US-PAY-008 (§8): approver queue of payroll runs awaiting approval; the page shows a
    // badge count.
    //
    // BUG-493: this gated on Payroll.Approve, which is NARROWER than the route — the child
    // carries no guard of its own, so anyone the '/payroll' parent admits can already open the
    // page by URL. The nav now says what the router does. If approvers really should be the
    // only ones in here, the fix is a permissionGuard(['Payroll.Approve']) on the ROUTE;
    // tightening access is a deliberate change, not something a nav gate should imply.
    label: 'Pending Approvals',
    route: '/payroll/approvals',
    module: 'Payroll',
    tenantRoles: ['HR Officer', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M16.704 4.153a.75.75 0 0 1 .143 1.052l-8 10.5a.75.75 0 0 1-1.127.075l-4.5-4.5a.75.75 0 0 1 1.06-1.06l3.894 3.893 7.48-9.817a.75.75 0 0 1 1.05-.143Z" clip-rule="evenodd"/></svg>`,
  },
  {
    // US-PAY-009 (§8): payroll reports + analytics; the reports page links across to the
    // analytics dashboard. No guard of its own — inherits the '/payroll' parent's role set.
    label: 'Payroll Reports',
    route: '/payroll/reports',
    module: 'Payroll',
    tenantRoles: ['HR Officer', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M15.5 2A1.5 1.5 0 0 1 17 3.5v13A1.5 1.5 0 0 1 15.5 18h-11A1.5 1.5 0 0 1 3 16.5v-13A1.5 1.5 0 0 1 4.5 2h11ZM6 13.25a.75.75 0 0 1 .75.75v.5a.75.75 0 0 1-1.5 0v-.5a.75.75 0 0 1 .75-.75Zm3-3a.75.75 0 0 1 .75.75v3.5a.75.75 0 0 1-1.5 0V11a.75.75 0 0 1 .75-.75Zm3-2a.75.75 0 0 1 .75.75v5.5a.75.75 0 0 1-1.5 0V9a.75.75 0 0 1 .75-.75ZM6.75 5.5a.75.75 0 0 0 0 1.5h6.5a.75.75 0 0 0 0-1.5h-6.5Z"/></svg>`,
  },
  {
    label: 'My Payslips',
    route: '/my-payslips',
    module: 'Payroll',
    // BUG-493: gated on the '/my-payslips' roleGuard, not on Payroll.View.Own. That permission
    // is still what the BACKEND checks per request; it simply is not what decides whether the
    // route opens, so it is not what should decide whether the link is shown.
    tenantRoles: ['Employee', 'HR Officer', 'Manager', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M4.5 2A1.5 1.5 0 0 0 3 3.5v14.793a.5.5 0 0 0 .724.447L6 17.618l2.276 1.122a.5.5 0 0 0 .448 0L11 17.618l2.276 1.122a.5.5 0 0 0 .448 0L16 17.618l2.276 1.122A.5.5 0 0 0 19 18.293V3.5A1.5 1.5 0 0 0 17.5 2h-13ZM6 6.75A.75.75 0 0 1 6.75 6h6.5a.75.75 0 0 1 0 1.5h-6.5A.75.75 0 0 1 6 6.75Zm0 3.5A.75.75 0 0 1 6.75 9.5h6.5a.75.75 0 0 1 0 1.5h-6.5A.75.75 0 0 1 6 10.25Zm.75 2.75a.75.75 0 0 0 0 1.5h3.5a.75.75 0 0 0 0-1.5h-3.5Z" clip-rule="evenodd"/></svg>`,
  },
  {
    label: 'Recruitment',
    route: '/recruitment',
    module: 'Recruitment',
    tenantRoles: ['HR Manager', 'HR Officer', 'Recruiter', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M10 9a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM6 8a2 2 0 1 1-4 0 2 2 0 0 1 4 0ZM1.49 15.326a.78.78 0 0 1-.358-.442 3 3 0 0 1 4.308-3.516 6.484 6.484 0 0 0-1.905 3.959c-.023.222-.014.442.025.654a4.97 4.97 0 0 1-2.07-.655ZM16.44 15.98a4.97 4.97 0 0 0 2.07-.654.78.78 0 0 0 .357-.442 3 3 0 0 0-4.308-3.517 6.484 6.484 0 0 1 1.907 3.96 2.32 2.32 0 0 1-.026.654ZM18 8a2 2 0 1 1-4 0 2 2 0 0 1 4 0ZM5.304 16.19a.844.844 0 0 1-.277-.71 5 5 0 0 1 9.947 0 .843.843 0 0 1-.277.71A6.975 6.975 0 0 1 10 18a6.974 6.974 0 0 1-4.696-1.81Z"/></svg>`,
  },
  {
    // US-PRF-001..010 (ISSUE-210): manager/HR/admin performance workspace.
    //
    // ISSUE-210 moved this from a single-permission gate to an any-of permission gate covering
    // roughly the same personas — the closest a proxy can get. BUG-493 finished the job: the
    // route roleGuards Manager + HR Officer + HR Manager + Tenant Admin, so the nav states
    // exactly that. The any-of list had still excluded Tenant Owner, who can enter.
    label: 'Performance',
    route: '/performance',
    module: 'Performance',
    tenantRoles: ['HR Manager', 'HR Officer', 'Manager', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M15.98 1.804a1 1 0 0 0-1.96 0l-.24 1.192a1 1 0 0 1-.784.785l-1.192.238a1 1 0 0 0 0 1.962l1.192.238a1 1 0 0 1 .785.785l.238 1.192a1 1 0 0 0 1.962 0l.238-1.192a1 1 0 0 1 .785-.785l1.192-.238a1 1 0 0 0 0-1.962l-1.192-.238a1 1 0 0 1-.785-.785l-.238-1.192ZM6.949 5.684a1 1 0 0 0-1.898 0l-.683 2.051a1 1 0 0 1-.633.633l-2.051.683a1 1 0 0 0 0 1.898l2.051.684a1 1 0 0 1 .633.632l.683 2.051a1 1 0 0 0 1.898 0l.683-2.051a1 1 0 0 1 .633-.633l2.051-.683a1 1 0 0 0 0-1.898l-2.051-.683a1 1 0 0 1-.633-.633L6.95 5.684ZM13.949 13.684a1 1 0 0 0-1.898 0l-.184.551a1 1 0 0 1-.632.633l-.551.183a1 1 0 0 0 0 1.898l.551.183a1 1 0 0 1 .633.633l.183.551a1 1 0 0 0 1.898 0l.184-.551a1 1 0 0 1 .632-.633l.551-.183a1 1 0 0 0 0-1.898l-.551-.184a1 1 0 0 1-.633-.632l-.183-.551Z"/></svg>`,
  },
  {
    // US-PRF-002 (ISSUE-210): employee self-service performance — goals + self-assessment.
    // The '/my-review' roleGuard admits Employee, which '/performance' does not; that is why
    // the two items exist separately, and each now mirrors its own route's guard (BUG-493).
    label: 'My Performance',
    route: '/my-review',
    module: 'Performance',
    tenantRoles: ['Employee', 'HR Officer', 'Manager', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M10 1c-1.716 0-3.408.106-5.07.31C3.806 1.45 3 2.414 3 3.517V16.75A2.25 2.25 0 0 0 5.25 19h9.5A2.25 2.25 0 0 0 17 16.75V3.517c0-1.103-.806-2.068-1.93-2.207A41.403 41.403 0 0 0 10 1ZM7.75 6.5a.75.75 0 0 0 0 1.5h4.5a.75.75 0 0 0 0-1.5h-4.5Zm0 3a.75.75 0 0 0 0 1.5h4.5a.75.75 0 0 0 0-1.5h-4.5Zm0 3a.75.75 0 0 0 0 1.5h2.5a.75.75 0 0 0 0-1.5h-2.5Z" clip-rule="evenodd"/></svg>`,
  },
  {
    // US-RPT-001: Pre-Built HR Reports catalog + viewer. The only item carrying BOTH gate
    // kinds, because its route has both: roleGuard([...]) in app.routes.ts AND
    // permissionGuard(['Reports.View']) on the feature's own index route. Dropping either half
    // would misstate who can get in (BUG-493).
    label: 'Reports',
    route: '/reports',
    module: 'Reporting',
    tenantRoles: ['HR Manager', 'HR Officer', 'Manager', 'Tenant Admin', 'Tenant Owner'],
    permission: 'Reports.View',
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M2 10a8 8 0 1 1 16 0 8 8 0 0 1-16 0Zm6.39-2.908a.75.75 0 0 1 .766.027l3.5 2.25a.75.75 0 0 1 0 1.262l-3.5 2.25A.75.75 0 0 1 8 12.25v-4.5a.75.75 0 0 1 .39-.658Z" clip-rule="evenodd"/></svg>`,
  },
  {
    // US-ONB-001: HR onboarding checklist templates.
    label: 'Onboarding',
    route: '/onboarding',
    module: 'Onboarding',
    tenantRoles: ['HR Manager', 'HR Officer', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M15.988 3.012A2.25 2.25 0 0 1 18 5.25v6.5A2.25 2.25 0 0 1 15.75 14H13.5l-2.69 2.69a.75.75 0 0 1-1.06 0L7.06 14H4.25A2.25 2.25 0 0 1 2 11.75v-6.5a2.25 2.25 0 0 1 2.012-2.238 41.493 41.493 0 0 1 11.976 0ZM6.75 6.5a.75.75 0 0 0 0 1.5h6.5a.75.75 0 0 0 0-1.5h-6.5Zm0 2.5a.75.75 0 0 0 0 1.5h3.5a.75.75 0 0 0 0-1.5h-3.5Z" clip-rule="evenodd"/></svg>`,
  },
  {
    // US-TRN-001: Training catalog + enrollment management. Any-of gate so
    // employees (View.Own), HR (View.All) and admins (Manage) all see it.
    label: 'Training',
    route: '/training',
    module: 'Training',
    permission: ['Training.View.Own', 'Training.View.All', 'Training.Manage'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M10.394 2.08a1 1 0 0 0-.788 0l-7 3a1 1 0 0 0 0 1.84L5.25 8.051a.999.999 0 0 1 .356-.257l4-1.714a1 1 0 1 1 .788 1.838L7.667 9.088l1.94.831a1 1 0 0 0 .787 0l7-3a1 1 0 0 0 0-1.838l-7-3ZM3.31 9.397 5 10.12v4.102a8.969 8.969 0 0 0-1.05-.174 1 1 0 0 1-.89-.89 11.115 11.115 0 0 1 .25-3.762ZM9.3 16.573A9.026 9.026 0 0 0 7 14.935v-3.957l1.818.78a3 3 0 0 0 2.364 0l5.508-2.361a11.026 11.026 0 0 1 .25 3.762 1 1 0 0 1-.89.89 8.968 8.968 0 0 0-5.35 2.524 1 1 0 0 1-1.4 0ZM6 18a1 1 0 0 0 1-1v-2.065a8.935 8.935 0 0 0-2-.712V17a1 1 0 0 0 1 1Z"/></svg>`,
  },
  {
    // US-TRN-002: Benefit-plan administration. Any-of gate so read-only users
    // (View.Own/View.All) and admins (Manage) all see it — MUST match the
    // /benefits route guard so nav visibility == route access (ISSUE-210).
    label: 'Benefits',
    route: '/benefits',
    module: 'Benefits',
    permission: ['Benefits.View.Own', 'Benefits.View.All', 'Benefits.Manage'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M10 1a4.5 4.5 0 0 0-4.5 4.5V9H5a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-6a2 2 0 0 0-2-2h-.5V5.5A4.5 4.5 0 0 0 10 1Zm3 8V5.5a3 3 0 1 0-6 0V9h6Z" clip-rule="evenodd"/></svg>`,
  },
  {
    // US-TRN-003: Employee self-service benefit enrollment. Gated on the
    // self permission ONLY — matching the /benefits/my-benefits child route
    // guard so nav visibility == route access (ISSUE-210).
    label: 'My Benefits',
    route: '/benefits/my-benefits',
    module: 'Benefits',
    permission: 'Benefits.View.Own',
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M9.661 2.237a.531.531 0 0 1 .678 0 11.947 11.947 0 0 0 7.078 2.749.5.5 0 0 1 .479.425c.069.52.104 1.05.104 1.59 0 5.162-3.26 9.563-7.834 11.256a.48.48 0 0 1-.332 0C4.923 16.549 1.66 12.148 1.66 6.986c0-.54.035-1.07.104-1.59a.5.5 0 0 1 .48-.425 11.947 11.947 0 0 0 7.417-2.734Zm4.502 5.771a.75.75 0 0 0-1.214-.882l-3.483 4.79-1.88-1.88a.75.75 0 1 0-1.06 1.061l2.5 2.5a.75.75 0 0 0 1.137-.089l4-5.5Z" clip-rule="evenodd"/></svg>`,
  },
  {
    // US-ADM-005: Tenant Admin user & role-assignment management.
    label: 'Users',
    route: '/admin/users',
    tenantRoles: ['Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M10 9a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM6 8a2 2 0 1 1-4 0 2 2 0 0 1 4 0ZM1.49 15.326a.78.78 0 0 1-.358-.442 3 3 0 0 1 4.308-3.516 6.484 6.484 0 0 0-1.905 3.959c-.023.222-.014.442.025.654a4.97 4.97 0 0 1-2.07-.655ZM16.44 15.98a4.97 4.97 0 0 0 2.07-.654.78.78 0 0 0 .357-.442 3 3 0 0 0-4.308-3.517 6.484 6.484 0 0 1 1.907 3.96 2.32 2.32 0 0 1-.026.654ZM18 8a2 2 0 1 1-4 0 2 2 0 0 1 4 0ZM5.304 16.19a.844.844 0 0 1-.277-.71 5 5 0 0 1 9.947 0 .843.843 0 0 1-.277.71A6.975 6.975 0 0 1 10 18a6.974 6.974 0 0 1-4.696-1.81Z"/></svg>`,
  },
  {
    label: 'Roles',
    route: '/admin/roles',
    tenantRoles: ['Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M10 1a4.5 4.5 0 0 0-4.5 4.5V9H5a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-6a2 2 0 0 0-2-2h-.5V5.5A4.5 4.5 0 0 0 10 1Zm3 8V5.5a3 3 0 1 0-6 0V9h6Z" clip-rule="evenodd"/></svg>`,
  },
  {
    // US-ADM-006: Tenant Admin company settings (org / branding / localization / policies).
    label: 'Settings',
    route: '/admin/settings',
    tenantRoles: ['Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M7.84 1.804A1 1 0 0 1 8.82 1h2.36a1 1 0 0 1 .98.804l.331 1.652a6.993 6.993 0 0 1 1.929 1.115l1.598-.54a1 1 0 0 1 1.186.447l1.18 2.044a1 1 0 0 1-.205 1.251l-1.267 1.113a7.047 7.047 0 0 1 0 2.228l1.267 1.113a1 1 0 0 1 .206 1.25l-1.18 2.045a1 1 0 0 1-1.187.447l-1.598-.54a6.993 6.993 0 0 1-1.929 1.115l-.33 1.652a1 1 0 0 1-.98.804H8.82a1 1 0 0 1-.98-.804l-.331-1.652a6.993 6.993 0 0 1-1.929-1.115l-1.598.54a1 1 0 0 1-1.186-.447l-1.18-2.044a1 1 0 0 1 .205-1.251l1.267-1.114a7.05 7.05 0 0 1 0-2.227L1.821 7.773a1 1 0 0 1-.206-1.25l1.18-2.045a1 1 0 0 1 1.187-.447l1.598.54A6.993 6.993 0 0 1 7.51 3.456l.33-1.652ZM10 13a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z" clip-rule="evenodd"/></svg>`,
  },
  {
    // US-CHR-012 / ISSUE-372 (E5): the Custom Fields designer shipped URL-only. Placed
    // beside Settings because its route lives at /settings/custom-fields. Gated with
    // `tenantRoles` for the same reason as Locations/Org Tree — the route is
    // roleGuard(['Tenant Admin']) and there is no Tenant.ManageCustomFields catalog key,
    // so Tenant.ManageSettings would be a proxy that can drift. 'Tenant Owner' listed
    // explicitly to match roleGuard's implicit TENANT_SUPER_ROLES widening.
    label: 'Custom Fields',
    route: '/settings/custom-fields',
    tenantRoles: ['Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M17 2.75a.75.75 0 0 0-1.5 0v5.5a.75.75 0 0 0 1.5 0v-5.5ZM17 15.75a.75.75 0 0 0-1.5 0v1.5a.75.75 0 0 0 1.5 0v-1.5ZM3.75 15a.75.75 0 0 1 .75.75v1.5a.75.75 0 0 1-1.5 0v-1.5a.75.75 0 0 1 .75-.75ZM4.5 2.75a.75.75 0 0 0-1.5 0v5.5a.75.75 0 0 0 1.5 0v-5.5ZM10 11a.75.75 0 0 1 .75.75v5.5a.75.75 0 0 1-1.5 0v-5.5A.75.75 0 0 1 10 11ZM10.75 2.75a.75.75 0 0 0-1.5 0v1.5a.75.75 0 0 0 1.5 0v-1.5ZM10 6a2 2 0 1 0 0 4 2 2 0 0 0 0-4ZM3.75 10a2 2 0 1 0 0 4 2 2 0 0 0 0-4ZM16.25 10a2 2 0 1 0 0 4 2 2 0 0 0 0-4Z"/></svg>`,
  },
  {
    // US-ADM-007: Tenant Admin approval-workflow definitions (per request type).
    label: 'Workflows',
    route: '/admin/workflows',
    tenantRoles: ['Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M3 3a1 1 0 0 1 1-1h3a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1H6v2h4V8a1 1 0 0 1 1-1h.5a1 1 0 0 1 0-2H11a1 1 0 0 1-1-1V3a1 1 0 0 1 1-1h3a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1h-1.5a1 1 0 0 0 0 2H13a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1h-3a1 1 0 0 1-1-1v-3a1 1 0 0 1 1-1h-4a1 1 0 0 1-1-1V7H4a1 1 0 0 1-1-1V3Z"/></svg>`,
  },
  {
    // US-ADM-008: audit-log viewer. Its roleGuard is the one tenant guard that admits Auditor,
    // so this is the only nav item listing that role (BUG-493: the old Audit.View gate did not
    // say so, and said nothing about Tenant Owner either).
    label: 'Audit Log',
    route: '/admin/audit-log',
    tenantRoles: ['Auditor', 'Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M10 1a4.5 4.5 0 0 0-4.5 4.5V9H5a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-6a2 2 0 0 0-2-2h-.5V5.5A4.5 4.5 0 0 0 10 1Zm3 8V5.5a3 3 0 1 0-6 0V9h6Z" clip-rule="evenodd"/></svg>`,
  },
  {
    // US-ADM-010: Tenant Admin on-demand data export (Data Management > Export).
    label: 'Data Export',
    route: '/admin/data-export',
    tenantRoles: ['Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M10 1a.75.75 0 0 1 .75.75v6.59l1.95-2.1a.75.75 0 1 1 1.1 1.02l-3.25 3.5a.75.75 0 0 1-1.1 0L6.2 7.26a.75.75 0 1 1 1.1-1.02l1.95 2.1V1.75A.75.75 0 0 1 10 1ZM5.273 11.5a.75.75 0 0 1 .727.927l-.6 2.4a.5.5 0 0 0 .485.673h8.23a.5.5 0 0 0 .485-.673l-.6-2.4a.75.75 0 0 1 1.454-.364l.6 2.4A2 2 0 0 1 14.515 17H6.27a2 2 0 0 1-1.94-2.48l.6-2.4a.75.75 0 0 1 .343-.62Z" clip-rule="evenodd"/></svg>`,
  },
  {
    // ISSUE-214 / US-NTF-002: tenant-admin notification-template editor. The page shipped
    // reachable by URL only — no nav entry anywhere — so a built feature was effectively
    // invisible. Role gate mirrors the route's roleGuard(['Tenant Admin','Tenant Owner']).
    label: 'Notification Templates',
    route: '/admin/notification-templates',
    tenantRoles: ['Tenant Admin', 'Tenant Owner'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M3 4a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V4Zm3 1.75a.75.75 0 0 1 .75-.75h6.5a.75.75 0 0 1 0 1.5h-6.5A.75.75 0 0 1 6 5.75Zm0 3.5A.75.75 0 0 1 6.75 8.5h6.5a.75.75 0 0 1 0 1.5h-6.5A.75.75 0 0 1 6 9.25Zm0 3.5a.75.75 0 0 1 .75-.75h3.5a.75.75 0 0 1 0 1.5h-3.5a.75.75 0 0 1-.75-.75Z"/></svg>`,
  },
  {
    // ISSUE-214 / US-NTF-003: personal per-tenant notification preferences. Same problem —
    // URL-only. No gate: the route carries no roleGuard because every authenticated user
    // manages their OWN preferences (the backend scopes to identity + membership, BR-4).
    label: 'Notification Preferences',
    route: '/profile/notification-preferences',
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M10 2a6 6 0 0 0-6 6v2.586l-.707.707A1 1 0 0 0 4 13h12a1 1 0 0 0 .707-1.707L16 10.586V8a6 6 0 0 0-6-6ZM8.05 15a2 2 0 0 0 3.9 0h-3.9Z"/></svg>`,
  },
  // ─── System Admin Console (platform persona only; role-gated, not permission) ──
  {
    // US-ADM-001: provision + manage tenants. SystemAdmin only (BR-1).
    label: 'Tenants',
    route: '/admin/tenants',
    systemRoles: ['SystemAdmin'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M4.25 2A2.25 2.25 0 0 0 2 4.25v2.5A2.25 2.25 0 0 0 4.25 9h2.5A2.25 2.25 0 0 0 9 6.75v-2.5A2.25 2.25 0 0 0 6.75 2h-2.5Zm0 9A2.25 2.25 0 0 0 2 13.25v2.5A2.25 2.25 0 0 0 4.25 18h2.5A2.25 2.25 0 0 0 9 15.75v-2.5A2.25 2.25 0 0 0 6.75 11h-2.5Zm9-9A2.25 2.25 0 0 0 11 4.25v2.5A2.25 2.25 0 0 0 13.25 9h2.5A2.25 2.25 0 0 0 18 6.75v-2.5A2.25 2.25 0 0 0 15.75 2h-2.5Zm0 9A2.25 2.25 0 0 0 11 13.25v2.5A2.25 2.25 0 0 0 13.25 18h2.5A2.25 2.25 0 0 0 18 15.75v-2.5A2.25 2.25 0 0 0 15.75 11h-2.5Z" clip-rule="evenodd"/></svg>`,
  },
  {
    // US-ADM-002: platform-health + tenant-usage monitoring — roleGuard(['SystemAdmin',
    // 'System Support']), the only two-role platform guard.
    //
    // BUG-493: while INavItem carried a single `role`, this could only say 'SystemAdmin'. A
    // System Support operator was therefore shown the entire TENANT menu (every link of which
    // its role is refused by) and never shown /admin/monitoring, the one route it can enter.
    label: 'Monitoring',
    route: '/admin/monitoring',
    systemRoles: ['System Support', 'SystemAdmin'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M15.5 2A1.5 1.5 0 0 1 17 3.5v13A1.5 1.5 0 0 1 15.5 18h-11A1.5 1.5 0 0 1 3 16.5v-13A1.5 1.5 0 0 1 4.5 2h11ZM6 13.25a.75.75 0 0 1 .75.75v.5a.75.75 0 0 1-1.5 0v-.5a.75.75 0 0 1 .75-.75Zm3-3a.75.75 0 0 1 .75.75v3.5a.75.75 0 0 1-1.5 0V11a.75.75 0 0 1 .75-.75Zm3-2a.75.75 0 0 1 .75.75v5.5a.75.75 0 0 1-1.5 0V9a.75.75 0 0 1 .75-.75Z"/></svg>`,
  },
  {
    // US-ADM-009: System Admin Console subscription plans (role-gated, not permission).
    label: 'Plans',
    route: '/admin/plans',
    systemRoles: ['SystemAdmin'],
    icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M1 4.25C1 3.56 1.56 3 2.25 3h15.5c.69 0 1.25.56 1.25 1.25v2.5C19 7.44 18.44 8 17.75 8H2.25C1.56 8 1 7.44 1 6.75v-2.5ZM1 11.25c0-.69.56-1.25 1.25-1.25h15.5c.69 0 1.25.56 1.25 1.25v4.5c0 .69-.56 1.25-1.25 1.25H2.25C1.56 17 1 16.44 1 15.75v-4.5Zm4 1a.75.75 0 0 0 0 1.5h2.5a.75.75 0 0 0 0-1.5H5Z"/></svg>`,
  },
];

@Component({
  selector: 'app-main-layout',
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    IdleTimeoutWarningComponent,
    ImpersonationBannerComponent,
    NotificationBellComponent,
    LogoFallbackDirective,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- US-ADM-003 NFR-4: persistent impersonation banner — a sibling of the
         routed content (and the shell) so no routed/tenant component can hide it. -->
    <app-impersonation-banner />
    <div class="main-layout" [class.sidebar-collapsed]="sidebarCollapsed()">
      <!-- Mobile overlay -->
      @if (mobileMenuOpen()) {
        <div
          class="mobile-overlay"
          (click)="mobileMenuOpen.set(false)"
        ></div>
      }

      <!-- Sidebar -->
      <aside
        class="sidebar"
        [class.sidebar-open]="mobileMenuOpen()"
        role="navigation"
        aria-label="Main navigation"
      >
        <!-- Sidebar header -->
        <div class="sidebar-header">
          <div class="tenant-switcher" [class.tenant-switcher-collapsed]="sidebarCollapsed()">
            <button
              type="button"
              class="tenant-trigger"
              [class.icon-only]="sidebarCollapsed()"
              [attr.aria-expanded]="tenantMenuOpen()"
              aria-haspopup="menu"
              [attr.aria-label]="tenantSwitchLabel()"
              (click)="toggleTenantMenu()"
            >
              <span class="tenant-logo">
                <span>{{ tenantInitial() }}</span>
                @if (currentTenantLogo()) {
                  <img [src]="currentTenantLogo()" [alt]="tenantName()" appLogoFallback />
                }
              </span>
              @if (!sidebarCollapsed()) {
                <span class="tenant-trigger-copy">
                  <span class="tenant-name">{{ tenantName() }}</span>
                  <span class="tenant-role">{{ currentPrimaryRole() }}</span>
                </span>
                <svg
                  xmlns="http://www.w3.org/2000/svg"
                  viewBox="0 0 20 20"
                  fill="currentColor"
                  class="tenant-chevron"
                  [class.rotate-180]="tenantMenuOpen()"
                >
                  <path
                    fill-rule="evenodd"
                    d="M5.22 8.22a.75.75 0 0 1 1.06 0L10 11.94l3.72-3.72a.75.75 0 1 1 1.06 1.06l-4.25 4.25a.75.75 0 0 1-1.06 0L5.22 9.28a.75.75 0 0 1 0-1.06Z"
                    clip-rule="evenodd"
                  />
                </svg>
              }
            </button>

            @if (tenantMenuOpen()) {
              <div class="tenant-menu" role="menu" aria-label="Organizations">
                <div class="tenant-menu-header">
                  <span>Organizations</span>
                  @if (tenantsLoading()) {
                    <span class="tenant-loading">Loading</span>
                  }
                </div>
                @if (tenantError()) {
                  <p class="tenant-error">{{ tenantError() }}</p>
                }
                @for (tenant of tenants(); track tenant.tenantId) {
                  <button
                    type="button"
                    class="tenant-option"
                    role="menuitemradio"
                    [class.current]="tenant.isCurrentTenant"
                    [class.unavailable]="!isTenantSwitchable(tenant)"
                    [disabled]="!isTenantSwitchable(tenant) || switchingTenantId() === tenant.tenantId"
                    [attr.aria-checked]="tenant.isCurrentTenant"
                    [attr.aria-disabled]="!isTenantSwitchable(tenant)"
                    [title]="tenantUnavailableMessage(tenant)"
                    (click)="switchTenant(tenant)"
                  >
                    <span class="tenant-logo option-logo">
                      <span>{{ tenantInitial(tenant.name) }}</span>
                      @if (tenant.logoUrl) {
                        <img [src]="tenant.logoUrl" [alt]="tenant.name" appLogoFallback />
                      }
                    </span>
                    <span class="tenant-option-copy">
                      <span class="tenant-option-name">{{ tenant.name }}</span>
                      <span class="tenant-option-role">{{ primaryRole(tenant) }}</span>
                    </span>
                    @if (tenant.status !== 'active') {
                      <span class="tenant-status">{{ statusLabel(tenant.status) }}</span>
                    }
                    @if (tenant.isCurrentTenant) {
                      <svg
                        xmlns="http://www.w3.org/2000/svg"
                        viewBox="0 0 20 20"
                        fill="currentColor"
                        class="tenant-check"
                        aria-hidden="true"
                      >
                        <path
                          fill-rule="evenodd"
                          d="M16.704 4.153a.75.75 0 0 1 .143 1.052l-8 10.5a.75.75 0 0 1-1.127.075l-4.5-4.5a.75.75 0 0 1 1.06-1.06l3.894 3.893 7.48-9.817a.75.75 0 0 1 1.05-.143Z"
                          clip-rule="evenodd"
                        />
                      </svg>
                    }
                  </button>
                } @empty {
                  @if (!tenantsLoading()) {
                    <p class="tenant-empty">No organization memberships found.</p>
                  }
                }
              </div>
            }
          </div>

          <!-- Collapse toggle (desktop) -->
          <button
            class="collapse-btn hidden lg:flex"
            (click)="toggleSidebar()"
            [attr.aria-label]="sidebarCollapsed() ? 'Expand sidebar' : 'Collapse sidebar'"
          >
            <svg
              xmlns="http://www.w3.org/2000/svg"
              viewBox="0 0 20 20"
              fill="currentColor"
              class="w-4 h-4 transition-transform"
              [class.rotate-180]="sidebarCollapsed()"
            >
              <path
                fill-rule="evenodd"
                d="M11.78 5.22a.75.75 0 0 1 0 1.06L8.06 10l3.72 3.72a.75.75 0 1 1-1.06 1.06l-4.25-4.25a.75.75 0 0 1 0-1.06l4.25-4.25a.75.75 0 0 1 1.06 0Z"
                clip-rule="evenodd"
              />
            </svg>
          </button>
        </div>

        <!-- Navigation items (pre-filtered by persona + permission in visibleNavItems) -->
        <nav class="sidebar-nav">
          @for (item of visibleNavItems(); track item.route) {
            <a
              [routerLink]="item.route"
              routerLinkActive="nav-active"
              class="nav-item"
              [title]="sidebarCollapsed() ? item.label : ''"
              (click)="mobileMenuOpen.set(false)"
            >
              <span class="nav-icon" [innerHTML]="item.icon"></span>
              @if (!sidebarCollapsed()) {
                <span class="nav-label">{{ item.label }}</span>
              }
            </a>
          }
        </nav>

        <!-- Sidebar footer -->
        <div class="sidebar-footer">
          <div class="user-menu">
            <div class="user-avatar">
              {{ userInitials() }}
            </div>
            @if (!sidebarCollapsed()) {
              <div class="user-info">
                <span class="user-name">
                  {{ authService.currentUser()?.displayName }}
                </span>
                <span class="user-email">
                  {{ authService.currentUser()?.email }}
                </span>
              </div>
              <button
                class="logout-btn"
                (click)="authService.logout()"
                aria-label="Log out"
                title="Log out"
              >
                <svg
                  xmlns="http://www.w3.org/2000/svg"
                  viewBox="0 0 20 20"
                  fill="currentColor"
                  class="w-4 h-4"
                >
                  <path
                    fill-rule="evenodd"
                    d="M3 4.25A2.25 2.25 0 0 1 5.25 2h5.5A2.25 2.25 0 0 1 13 4.25v2a.75.75 0 0 1-1.5 0v-2a.75.75 0 0 0-.75-.75h-5.5a.75.75 0 0 0-.75.75v11.5c0 .414.336.75.75.75h5.5a.75.75 0 0 0 .75-.75v-2a.75.75 0 0 1 1.5 0v2A2.25 2.25 0 0 1 10.75 18h-5.5A2.25 2.25 0 0 1 3 15.75V4.25Z"
                    clip-rule="evenodd"
                  />
                  <path
                    fill-rule="evenodd"
                    d="M19 10a.75.75 0 0 0-.75-.75H8.704l1.048-.943a.75.75 0 1 0-1.004-1.114l-2.5 2.25a.75.75 0 0 0 0 1.114l2.5 2.25a.75.75 0 1 0 1.004-1.114l-1.048-.943h9.546A.75.75 0 0 0 19 10Z"
                    clip-rule="evenodd"
                  />
                </svg>
              </button>
            }
          </div>
        </div>
      </aside>

      <!-- Main content area -->
      <div class="main-content">
        <!-- Top bar -->
        <header class="topbar">
          <!-- Mobile menu toggle -->
          <button
            class="mobile-menu-btn lg:hidden"
            (click)="mobileMenuOpen.set(true)"
            aria-label="Open navigation menu"
          >
            <svg
              xmlns="http://www.w3.org/2000/svg"
              viewBox="0 0 20 20"
              fill="currentColor"
              class="w-5 h-5"
            >
              <path
                fill-rule="evenodd"
                d="M2 4.75A.75.75 0 0 1 2.75 4h14.5a.75.75 0 0 1 0 1.5H2.75A.75.75 0 0 1 2 4.75Zm0 10.5a.75.75 0 0 1 .75-.75h7.5a.75.75 0 0 1 0 1.5h-7.5a.75.75 0 0 1-.75-.75ZM2 10a.75.75 0 0 1 .75-.75h14.5a.75.75 0 0 1 0 1.5H2.75A.75.75 0 0 1 2 10Z"
                clip-rule="evenodd"
              />
            </svg>
          </button>

          <button
            type="button"
            class="mobile-tenant-trigger lg:hidden"
            (click)="toggleTenantMenu()"
            [attr.aria-expanded]="tenantMenuOpen()"
            aria-haspopup="menu"
            [attr.aria-label]="tenantSwitchLabel()"
          >
            <span class="tenant-logo">
              <span>{{ tenantInitial() }}</span>
              @if (currentTenantLogo()) {
                <img [src]="currentTenantLogo()" [alt]="tenantName()" appLogoFallback />
              }
            </span>
            <span class="mobile-tenant-name">{{ tenantName() }}</span>
          </button>

          @if (tenantMenuOpen()) {
            <div class="tenant-menu mobile-tenant-menu" role="menu" aria-label="Organizations">
              <div class="tenant-menu-header">
                <span>Organizations</span>
                @if (tenantsLoading()) {
                  <span class="tenant-loading">Loading</span>
                }
              </div>
              @if (tenantError()) {
                <p class="tenant-error">{{ tenantError() }}</p>
              }
              @for (tenant of tenants(); track tenant.tenantId) {
                <button
                  type="button"
                  class="tenant-option"
                  role="menuitemradio"
                  [class.current]="tenant.isCurrentTenant"
                  [class.unavailable]="!isTenantSwitchable(tenant)"
                  [disabled]="!isTenantSwitchable(tenant) || switchingTenantId() === tenant.tenantId"
                  [attr.aria-checked]="tenant.isCurrentTenant"
                  [attr.aria-disabled]="!isTenantSwitchable(tenant)"
                  [title]="tenantUnavailableMessage(tenant)"
                  (click)="switchTenant(tenant)"
                >
                  <span class="tenant-logo option-logo">
                    <span>{{ tenantInitial(tenant.name) }}</span>
                    @if (tenant.logoUrl) {
                      <img [src]="tenant.logoUrl" [alt]="tenant.name" appLogoFallback />
                    }
                  </span>
                  <span class="tenant-option-copy">
                    <span class="tenant-option-name">{{ tenant.name }}</span>
                    <span class="tenant-option-role">{{ primaryRole(tenant) }}</span>
                  </span>
                  @if (tenant.status !== 'active') {
                    <span class="tenant-status">{{ statusLabel(tenant.status) }}</span>
                  }
                  @if (tenant.isCurrentTenant) {
                    <svg
                      xmlns="http://www.w3.org/2000/svg"
                      viewBox="0 0 20 20"
                      fill="currentColor"
                      class="tenant-check"
                      aria-hidden="true"
                    >
                      <path
                        fill-rule="evenodd"
                        d="M16.704 4.153a.75.75 0 0 1 .143 1.052l-8 10.5a.75.75 0 0 1-1.127.075l-4.5-4.5a.75.75 0 0 1 1.06-1.06l3.894 3.893 7.48-9.817a.75.75 0 0 1 1.05-.143Z"
                        clip-rule="evenodd"
                      />
                    </svg>
                  }
                </button>
              } @empty {
                @if (!tenantsLoading()) {
                  <p class="tenant-empty">No organization memberships found.</p>
                }
              }
            </div>
          }

          <div class="topbar-spacer"></div>

          <!-- Top bar right actions -->
          <div class="topbar-actions">
            <!-- US-NTF-001: real-time notification bell + panel -->
            <app-notification-bell />

            <!-- Mobile logout -->
            <button
              class="logout-btn-mobile lg:hidden"
              (click)="authService.logout()"
              aria-label="Log out"
            >
              <svg
                xmlns="http://www.w3.org/2000/svg"
                viewBox="0 0 20 20"
                fill="currentColor"
                class="w-5 h-5"
              >
                <path
                  fill-rule="evenodd"
                  d="M3 4.25A2.25 2.25 0 0 1 5.25 2h5.5A2.25 2.25 0 0 1 13 4.25v2a.75.75 0 0 1-1.5 0v-2a.75.75 0 0 0-.75-.75h-5.5a.75.75 0 0 0-.75.75v11.5c0 .414.336.75.75.75h5.5a.75.75 0 0 0 .75-.75v-2a.75.75 0 0 1 1.5 0v2A2.25 2.25 0 0 1 10.75 18h-5.5A2.25 2.25 0 0 1 3 15.75V4.25Z"
                  clip-rule="evenodd"
                />
                <path
                  fill-rule="evenodd"
                  d="M19 10a.75.75 0 0 0-.75-.75H8.704l1.048-.943a.75.75 0 1 0-1.004-1.114l-2.5 2.25a.75.75 0 0 0 0 1.114l2.5 2.25a.75.75 0 1 0 1.004-1.114l-1.048-.943h9.546A.75.75 0 0 0 19 10Z"
                  clip-rule="evenodd"
                />
              </svg>
            </button>
          </div>
        </header>

        <!-- Page content -->
        <main class="page-content">
          <router-outlet />
        </main>
      </div>

      <!-- Idle timeout warning modal (US-AUTH-009 BR-6) -->
      <app-idle-timeout-warning />
    </div>
  `,
  styles: [`
    .main-layout {
      @apply flex min-h-screen bg-surface-secondary;
    }

    /* ─── Sidebar ──────────────────────────────── */

    .sidebar {
      @apply fixed top-0 left-0 z-40 flex h-full w-64 flex-col
        border-r border-neutral-100 bg-white transition-all duration-200;
      @apply lg:relative;

      /* Off-screen on mobile by default */
      @apply -translate-x-full lg:translate-x-0;
    }

    .sidebar-open {
      @apply translate-x-0;
    }

    .sidebar-collapsed .sidebar {
      @apply w-16;
    }

    .mobile-overlay {
      @apply fixed inset-0 z-30 bg-black/20 backdrop-blur-sm lg:hidden;
    }

    .sidebar-header {
      @apply relative flex items-center justify-between gap-2 px-4 py-4 border-b border-neutral-50;
    }

    .tenant-switcher {
      @apply relative min-w-0 flex-1;
    }

    .tenant-switcher-collapsed {
      @apply flex-none;
    }

    .tenant-trigger,
    .mobile-tenant-trigger {
      @apply flex min-w-0 items-center gap-2 rounded-lg border border-transparent
        text-left transition-colors duration-150 hover:bg-neutral-50
        focus:outline-none focus:ring-2 focus:ring-brand-500 focus:ring-offset-2;
    }

    .tenant-trigger {
      @apply w-full px-2 py-1.5;
    }

    .tenant-trigger.icon-only {
      @apply h-9 w-9 justify-center p-0;
    }

    .tenant-logo {
      @apply relative flex h-8 w-8 flex-shrink-0 items-center justify-center overflow-hidden
        rounded-lg bg-brand-600 text-xs font-semibold text-white;
    }

    /* ISSUE-204: logo overlays the initial; appLogoFallback hides it on 404 so the initial shows. */
    .tenant-logo img {
      @apply absolute inset-0 h-full w-full object-cover;
    }

    .tenant-trigger-copy,
    .tenant-option-copy {
      @apply min-w-0 flex-1;
    }

    .tenant-name,
    .tenant-option-name {
      @apply block truncate text-sm font-semibold text-neutral-900;
    }

    .tenant-role,
    .tenant-option-role {
      @apply block truncate text-xs text-neutral-500;
    }

    .tenant-chevron {
      @apply h-4 w-4 flex-shrink-0 text-neutral-400 transition-transform duration-150;
    }

    .tenant-menu {
      @apply absolute left-3 right-3 top-full z-50 mt-2 rounded-xl border border-neutral-100
        bg-white p-2 shadow-lg;
    }

    .tenant-menu-header {
      @apply flex items-center justify-between px-2 pb-2 text-xs font-semibold uppercase
        tracking-wide text-neutral-400;
    }

    .tenant-loading {
      @apply normal-case tracking-normal text-brand-600;
    }

    .tenant-error,
    .tenant-empty {
      @apply m-0 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700;
    }

    .tenant-empty {
      @apply bg-neutral-50 text-neutral-500;
    }

    .tenant-option {
      @apply flex w-full items-center gap-2 rounded-lg px-2 py-2 text-left
        transition-colors duration-150 hover:bg-neutral-50 focus:outline-none
        focus:ring-2 focus:ring-brand-500 focus:ring-offset-1;
    }

    .tenant-option.current {
      @apply bg-brand-50;
    }

    .tenant-option.unavailable {
      @apply cursor-not-allowed opacity-55 grayscale hover:bg-transparent;
    }

    .option-logo {
      @apply h-9 w-9;
    }

    .tenant-status {
      @apply rounded-full bg-neutral-100 px-2 py-0.5 text-xs font-medium text-neutral-600;
    }

    .tenant-check {
      @apply h-4 w-4 flex-shrink-0 text-brand-600;
    }

    .collapse-btn {
      @apply w-7 h-7 rounded-md flex items-center justify-center
        text-neutral-400 hover:text-neutral-600 hover:bg-neutral-100
        transition-colors;
    }

    /* ─── Navigation ───────────────────────────── */

    .sidebar-nav {
      @apply flex-1 overflow-y-auto px-2 py-3 space-y-0.5;
    }

    .nav-item {
      @apply flex items-center gap-3 px-3 py-2 rounded-lg text-sm
        text-neutral-600 hover:bg-neutral-50 hover:text-neutral-900
        transition-colors duration-150 cursor-pointer no-underline;
    }

    .sidebar-collapsed .nav-item {
      @apply justify-center px-0;
    }

    .nav-active {
      @apply bg-brand-50 text-brand-700 font-medium;
    }

    .nav-icon {
      @apply flex-shrink-0 w-5 h-5;

      :host ::ng-deep svg {
        @apply w-5 h-5;
      }
    }

    .nav-label {
      @apply truncate;
    }

    /* ─── Sidebar footer ──────────────────────── */

    .sidebar-footer {
      @apply border-t border-neutral-50 px-3 py-3;
    }

    .user-menu {
      @apply flex items-center gap-2.5;
    }

    .user-avatar {
      @apply flex-shrink-0 w-8 h-8 rounded-full bg-brand-100 text-brand-700
        text-xs font-semibold flex items-center justify-center;
    }

    .user-info {
      @apply flex-1 min-w-0;
    }

    .user-name {
      @apply block text-sm font-medium text-neutral-900 truncate leading-tight;
    }

    .user-email {
      @apply block text-xs text-neutral-400 truncate leading-tight;
    }

    .logout-btn {
      @apply flex-shrink-0 w-7 h-7 rounded-md flex items-center justify-center
        text-neutral-400 hover:text-red-600 hover:bg-red-50 transition-colors;
    }

    /* ─── Main content ─────────────────────────── */

    .main-content {
      @apply flex-1 flex flex-col min-w-0;
    }

    .sidebar-collapsed .main-content {
      @apply lg:ml-0;
    }

    .topbar {
      @apply relative flex items-center h-14 px-4 border-b border-neutral-100 bg-white;
      @apply lg:px-6;
    }

    .mobile-menu-btn {
      @apply w-9 h-9 rounded-lg flex items-center justify-center
        text-neutral-500 hover:bg-neutral-100 transition-colors;
    }

    .mobile-tenant-trigger {
      @apply ml-2 max-w-[calc(100vw-8rem)] px-2 py-1;
    }

    .mobile-tenant-name {
      @apply truncate text-sm font-semibold text-neutral-900;
    }

    .mobile-tenant-menu {
      @apply left-3 right-3 top-14 lg:hidden;
    }

    .topbar-spacer {
      @apply flex-1;
    }

    .topbar-actions {
      @apply flex items-center gap-2;
    }

    .logout-btn-mobile {
      @apply w-9 h-9 rounded-lg flex items-center justify-center
        text-neutral-500 hover:text-red-600 hover:bg-red-50 transition-colors;
    }

    .page-content {
      @apply flex-1 overflow-y-auto;
    }
  `],
})
export class MainLayoutComponent implements OnInit {
  readonly authService = inject(AuthService);
  private readonly tenantService = inject(TenantService);
  private readonly idleTimeoutService = inject(IdleTimeoutService);

  readonly sidebarCollapsed = signal(false);
  readonly mobileMenuOpen = signal(false);
  readonly tenantMenuOpen = signal(false);
  readonly tenants = signal<IUserTenant[]>([]);
  readonly tenantsLoading = signal(false);
  readonly tenantError = signal('');
  readonly switchingTenantId = signal<string | null>(null);

  readonly navItems: INavItem[] = NAV_ITEMS;

  /**
   * Persona-aware sidebar (US-ADM-001..009). A platform operator runs the System Admin Console
   * (Tenants / Monitoring / Plans) and must NOT see the tenant-HR menu, whose routes it cannot
   * enter — they guard on tenant roles it doesn't hold, and roleGuard deliberately does NOT widen
   * a system persona into tenant features. Conversely tenant users never see the system-console
   * items. Within each persona, items gate on exactly what their route's guard checks.
   *
   * BUG-493: "is this a platform operator?" is answered with the guards' own SYSTEM_ROLES list,
   * not a hardcoded 'SystemAdmin'. Deriving it separately is what stranded the System Support
   * persona — hasRole('SystemAdmin') was false for them, so they got the full tenant menu (every
   * link dead-ending at /forbidden) while /admin/monitoring, the one route their role admits, was
   * never shown.
   */
  visibleNavItems(): INavItem[] {
    const isSystemPersona = SYSTEM_ROLES.some((r) => this.authService.hasRole(r));
    return this.navItems.filter((item) => {
      const isSystemItem = !!item.systemRoles;
      if (isSystemPersona !== isSystemItem) {
        return false; // platform operator → system items only; tenant user → tenant items only
      }
      // Mirror the platform roleGuard on the item's route (e.g. /admin/monitoring admits both
      // SystemAdmin and System Support, /admin/plans only SystemAdmin).
      if (item.systemRoles && !item.systemRoles.some((r) => this.authService.hasRole(r))) {
        return false;
      }
      // US-ADM-012 AC-2: hide items whose module is not entitled by the tenant plan.
      // isModuleEntitled fails OPEN, so this only ever hides an item when the tenant
      // has an authoritative canonical module list that omits this module.
      if (item.module && !isModuleEntitled(item.module, this.tenantService.enabledModules())) {
        return false;
      }
      // ISSUE-214: mirror a route's roleGuard so nav visibility == route access.
      if (item.tenantRoles && !item.tenantRoles.some((r) => this.authService.hasRole(r))) {
        return false;
      }
      if (!item.permission) {
        return true;
      }
      return Array.isArray(item.permission)
        ? this.authService.hasAnyPermission(item.permission)
        : this.authService.hasPermission(item.permission);
    });
  }

  ngOnInit(): void {
    this.loadTenants();
    this.initIdleTimeout();
  }

  userInitials(): string {
    const name = this.authService.currentUser()?.displayName || '';
    const parts = name.split(' ');
    if (parts.length >= 2) {
      return (parts[0][0] + parts[1][0]).toUpperCase();
    }
    return name.substring(0, 2).toUpperCase();
  }

  toggleSidebar(): void {
    this.sidebarCollapsed.update((v) => !v);
  }

  toggleTenantMenu(): void {
    this.tenantMenuOpen.update((open) => !open);
    if (!this.tenants().length && !this.tenantsLoading()) {
      this.loadTenants();
    }
  }

  switchTenant(tenant: IUserTenant): void {
    if (!this.isTenantSwitchable(tenant) || this.switchingTenantId()) {
      return;
    }

    this.switchingTenantId.set(tenant.tenantId);
    this.tenantError.set('');

    this.authService
      .switchTenant({ tenantId: tenant.tenantId })
      .pipe(
        catchError((error) => {
          this.tenantError.set(
            error?.error?.message ||
              error?.error?.error ||
              'This organization is unavailable right now.'
          );
          return EMPTY;
        }),
        finalize(() => this.switchingTenantId.set(null))
      )
      .subscribe();
  }

  isTenantSwitchable(tenant: IUserTenant): boolean {
    return !tenant.isCurrentTenant && (tenant.status === 'active' || tenant.status === 'trial');
  }

  primaryRole(tenant: IUserTenant): string {
    return tenant.roles[0] || 'Member';
  }

  currentPrimaryRole(): string {
    const currentTenantId = this.authService.currentTenant()?.tenantId;
    const current = this.tenants().find((tenant) => tenant.tenantId === currentTenantId || tenant.isCurrentTenant);
    return current ? this.primaryRole(current) : this.authService.roles()[0] || 'Member';
  }

  currentTenantLogo(): string | undefined {
    const currentTenantId = this.authService.currentTenant()?.tenantId;
    return (
      this.tenants().find((tenant) => tenant.tenantId === currentTenantId || tenant.isCurrentTenant)?.logoUrl ||
      this.authService.currentTenant()?.logoUrl ||
      this.tenantService.tenantContext().logoUrl
    );
  }

  tenantInitial(name = this.tenantName()): string {
    return name.trim().charAt(0).toUpperCase() || 'H';
  }

  statusLabel(status: IUserTenant['status']): string {
    return status
      .split('_')
      .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
      .join(' ');
  }

  tenantUnavailableMessage(tenant: IUserTenant): string {
    if (tenant.isCurrentTenant) {
      return 'Current organization';
    }

    if (this.isTenantSwitchable(tenant)) {
      return `Switch to ${tenant.name}`;
    }

    return `${tenant.name} is ${this.statusLabel(tenant.status)} and cannot be opened.`;
  }

  tenantName(): string {
    return this.authService.currentTenant()?.name || this.tenantService.displayName();
  }

  /**
   * Accessible name for the tenant-switcher triggers (ISSUE-205 / WCAG 2.5.3 Label
   * in Name): the button visibly renders the tenant name, so its accessible name
   * must contain that visible text — not just the action verb "Switch organization".
   */
  tenantSwitchLabel(): string {
    return `Switch organization, ${this.tenantName()}`;
  }

  /** Load tenant auth settings and start idle timeout tracking (US-AUTH-009). */
  private initIdleTimeout(): void {
    this.authService.getTenantAuthSettings().subscribe({
      next: (settings) => {
        const timeout = settings.idleTimeoutMinutes ?? 60;
        if (timeout > 0) {
          this.idleTimeoutService.start(timeout);
        }
      },
      error: () => {
        // Fallback to default 60 min idle timeout on settings load failure
        this.idleTimeoutService.start(60);
      },
    });
  }

  private loadTenants(): void {
    this.tenantsLoading.set(true);
    this.tenantError.set('');

    this.authService
      .getMyTenants()
      .pipe(
        catchError(() => {
          this.tenantError.set('Unable to load organization memberships.');
          return EMPTY;
        }),
        finalize(() => this.tenantsLoading.set(false))
      )
      .subscribe((tenants) => {
        this.tenants.set(tenants);
      });
  }
}
