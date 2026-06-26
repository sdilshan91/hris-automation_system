---
name: us-lv-007-holiday-findings
description: US-LV-007 holiday-calendar test pass (2026-06-25) — route, authz matrix, BUG-031/032 + BUG-003 extended, balance-preview trick, stale TC dates
metadata:
  type: project
---

# US-LV-007 Holiday Calendar — representative API pass 2026-06-25 (19P / 2F / 3B of 24 owned)

REPORT-ONLY run. Real route is `/api/v1/holidays` (NOT `/tenant/...`): GET (Holiday.View), POST (Holiday.Create), PUT/{id} (Holiday.Edit), POST/{id}/deactivate (Holiday.Deactivate), POST/import (Holiday.Import). No hard-delete (DELETE → 405).

**Authz matrix (correct, source PermissionCatalog.cs):** TenantAdmin / HRManager / **HROfficer** all have full Holiday.* → HR Officer is NOT 403 here (unlike entitlements BUG-027). Manager + Employee = Holiday.View only (read 200, all writes 403). Unauthenticated/bad-token = 401. So acme `hr@acme.test` CAN create/edit/import.

**New findings filed:**
- **BUG-031 MED** — entire Holidays feature never calls IAuditService (HolidayService.cs has no IAuditService dep); create/update/deactivate/import write ZERO audit_logs rows (row UpdatedBy stamp ≠ change-history). Same class as BUG-025 (leave-types) / BUG-028 (entitlements).
- **BUG-032 MED** — LIST `?locationId=` filter uses strict equality `h.LocationId == locationId` (HolidayService.cs:234-235) → DROPS tenant-wide (null-location) holidays. `?locationId=London`(no London rows) returns `[]`. But the leave-exclusion **provider** (HolidayProvider.cs) gets it RIGHT (`LocationId==null || ==loc`). Divergence: list wrong, provider correct.
- **BUG-003 EXTENDED** (not re-filed) — acme token + `X-Tenant-Subdomain: techoneglobal` → READ 200 (foreign ctx accepted) AND WRITE 201 (created row in techoneglobal). Config CRUD surface = BUG-003 reproduces. ISO TC FAILED.
- **ISSUE-040 LOW** — default list (no activeOnly param) returns deactivated rows; handler filters only when `activeOnly==true` (HolidayService.cs:237-238). TC-143 assumes default=active-only; they disagree.

**Verdicts good:** uniqueness BR-1 works (per tenant+location AND tenant-wide null via partial indexes; dup date 400, same date different location 201, tenant-wide dup 400). Day-count exclusion works + type-sensitive: Public excluded (5→4), Restricted/Optional NOT (stay 5) — verified via `GET /api/v1/leaves/balance-preview?leaveTypeId&startDate&endDate` (computes requestedDays WITHOUT persisting — use this to avoid "already have a request for these dates" 409). CSV import: 20-row=created 20, dup flagging (DB-dup + in-file dup both reported), invalid type row-error, idempotent re-run created 0; 100-row in 240ms (<5s NFR-3). Read P95 15ms (<200ms NFR-1). XSS/SQLi neutralized (stored inert, params 400). EF query filter isolates (acme reads only acme).

**Gotchas / carry-forward:**
- TC hardcoded dates are STALE vs "now" (2026-06-25): leave-apply rejects dates >7 days past, and prior runs left overlapping leave requests in Jun/Jul/Aug. Use balance-preview (no persistence, no overlap conflict) and/or future weeks for day-count checks.
- BLOCKED: TC-137 + TC-148 (calendar UI / a11y — fe-platform-bound, FE pinned to platform subdomain). TC-141 (recurring Hangfire HolidayRecurrenceJob — not on-demand API-triggerable, would touch ALL tenants; recurrence service logic verified by code review: idempotent by (date,locationId) key, AddYearSafe clamps Feb-29). TC-144 (FR-5 seeding — onboarding wizard call site UNWIRED, TODO(onboarding)).
- TC-133 (per-location employee split) only partially live — no location-assigned employee personas seeded (employee@acme has locationId=null); core scoping proven (tenant-wide drops a day, NY-only does not, for the no-location emp).

**acme RESIDUE to clean (created this run):** ~25 holidays 2026/2027/2028 (New Year's, Thanksgiving, Spring Bank, Local Festival, City Day, NY New Year, 3 TC-142 types, St.Patrick's[deactivated], Provisional×2, Year-End[deactivated], TC131/132/133 holidays, AuthZ probes, XSS rows `<script>...`/`=cmd|calc`, h20/dup/h100 CSV imports). **techoneglobal residue (BUG-003 write probe): holiday id `019efe6d-3b30-75d4-8e1a-6e3438116529` (2099-01-01, deactivated but row persists) — needs DB hard-delete.**

See [[us-chr-009-status-findings]] (BUG-003 class), [[us-adm-006-settings-findings]] (canonical BUG-003), [[qa-no-debugger-for-perf]].
