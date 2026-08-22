---
name: admin-console-fe-pass-2026-06-30
description: Admin Console FE per-TC pass — acme tenant pages now render in FE; 2 TC flips + 4 new findings; platform subdomain login 404s
metadata:
  type: project
---

# Admin Console FE per-TC pass 2026-06-30 (Playwright MCP, REPORT-ONLY)

**BREAKTHROUGH vs prior runs:** `http://acme.myhrm.org:4200` now serves real ACME tenant data to
`tenantadmin@acme.test` / `Admin@123!` — FE is NO LONGER pinned to `platform`. So the tenant-admin admin
pages finally RENDER for FE/a11y TCs (all prior admin a11y TCs were `[b] playwright-mcp-down` / FE-pinned).

**Why:** earlier admin-console runs marked acme-data UI/a11y TCs blocked because FE was pinned to platform.
**How to apply:** for future admin-console FE TCs, drive `acme.myhrm.org:4200` as tenantadmin — pages render.

## Verified rendering (in-app sidebar nav, acme/tenantadmin)
Users (8-row list + actions), Roles (8 built-in cards, accurate counts, custom empty-state), Settings
(4 labeled tabs, dirty-tracking Save works, branding hex picker), Workflows (empty-state), Audit Log
(1426 rows, working pagination `Showing 1–50 of 1426`, detail diff-view), Data Export (form renders).

## TC flips
- **TC-ADM-008-16 → pass**: audit detail dialog Changes section groups Added/Removed/Modified, each field
  text-labeled "ADDED"+"+" (not color-only). No-change row → graceful "no field-level changes" empty-state.
- **TC-ADM-006-17 → fail**: settings a11y — bad-hex error visible but NOT programmatically associated
  (hex input aria-invalid=null, aria-describedby=null) + 3 file inputs nameless → ISSUE-212.

## NEW findings (next free were BUG-103 / ISSUE-211 / ENH-024)
- **BUG-104 HIGH** (FE) — Data Export UI dead: FE `data-export.service.ts:50` calls `/api/v1/tenant/exports`,
  BE route is `/api/v1/tenant/data-exports` (DataExportController.cs:20) — missing `data-` segment on BOTH
  tenant + system paths. History 404→false "No exports yet"; Start Export posts to dead route. Same
  [[fe-be-tenant-url-prefix-mismatch]] class.
- **BUG-103 MED** (FE+BE) — Users page: pagination renders `Showing 1–NaN of {{total}}` / `1 / NaN` (total
  field undefined in paging envelope) + role filter empty because `GET /tenant/users/assignable-roles`→405.
- **ISSUE-212 MED** (FE a11y) — settings bad-hex error not aria-associated + nameless file inputs (TC-006-17).
- **ISSUE-211 LOW** (FE i18n) — Users status cells render raw key `userManagement.status.Active`/`.Disabled`.

## Platform pages STAY blocked — login 404
`http://admin.myhrm.org:4200` FE serves, but login as `admin@hrm.local` → POST /api/v1/auth/login **404
"This workspace does not exist"** — the `admin` subdomain does NOT resolve to the system tenant in this dev
env. So admin/tenants, admin/plans, admin/monitoring + impersonation banner are unreachable. Recorded as
`platform-login-unreachable` exec_notes on TC-ADM-001-11 / 002-13 / 003-12.

## Systemic (referenced, NOT re-filed)
BUG-096, BUG-097, ISSUE-204 (branding logo 404 every page — `/{tenantId}/branding/logo.png`), ISSUE-205.

## Discipline
acme settings NOT mutated — typed dirty edits discarded by in-app nav, never Saved. 0 residue. IN-APP nav
only (BUG-097: hard navigate/reload to a protected URL logs out); used soft routerLink clicks via evaluate.
