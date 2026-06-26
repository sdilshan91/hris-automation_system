---
name: us-lv-004-pending-queue-findings
description: US-LV-004 manager pending-leave-queue 2026-06-25 CLEAN run (22P/5B/0 findings) + manager-link/reports seed recipe + why BUG-003 does NOT extend to this read endpoint
metadata:
  type: project
---

US-LV-004 manager pending-leave-queue test pass — 2026-06-25. **Clean run: 22 PASS / 5 BLOCKED / 0 new findings.** First LV story with zero defects.

**Real route/perm:** `GET /api/v1/leaves/pending` on `LeaveRequestsController` (`[Route("api/v1/leaves")]`), perm `Leave.Approve.Team`. Query params are `leaveTypeId, employeeId, startDate, endDate, sortBy(requestedAt|startDate), sortAscending(bool), page, pageSize` — NOT the TCs' `from/to/sortDir`. Handler: `LeaveRequestService.GetPendingForManagerAsync` (`LeaveRequestService.cs:346`).

**Seed recipe (data dependency):** manager@acme.test had NO employee row; John Doe reported to nobody. Seeded employee `019eff00-0000-7000-8000-000000000001` (EMP-MGR01, Team Manager, dept/job reused from John, user_id=manager's `019efa61-e620-...`), then set John Doe `reports_to_employee_id` → that manager. Manager resolves via `Employee.UserId == currentUser.UserId`; scope = `Employees.Where(ReportsToEmployeeId == manager.Id)`. **RESIDUE LEFT:** EMP-MGR01 employee row + John Doe's reports_to FK + **Et Contract (019efcfb-e042-...) repointed from John→manager** (was John's report; flag for cleanup). Test leave_request rows I added (approved-overlap/overdue/arrival) were DELETED; back to 6 original pending.

**Why BUG-003 does NOT extend here (KEY):** acme manager JWT + `X-Tenant-Subdomain: techoneglobal` returns **empty (total=0)**, not a leak. Unlike BUG-003 write surfaces, this read resolves the manager employee UNDER the spoofed tenant's global query filter — the manager's employee row is in acme, so under techoneglobal context no employee matches → empty queue, and the LeaveRequest global filter scopes to techoneglobal. No `IgnoreQueryFilters` in the handler. So this endpoint is isolation-safe. (techoneglobal also had 0 pending anyway.) ISO-013/014/015/016 all PASS.

**Verified correct:** balance-inline matches ledger exactly (Annual=20.0, Sick=5.0 from `leave_ledger.balance_after`; Redis NFR-2 deferred, ledger is source of truth); FR-5 team-conflict count (excludes requester, counts approved-overlap teammates); BR-3 overdue (>30d strict); filters (type/employee/date AND-compose); pagination (slice + total + beyond-page-empty); pageSize clamp 200→50, page/size 0/-1→defaults, abc→400; injection (non-uuid guid→400, sortBy whitelist fallback, table intact); 401 no-token / 403 non-approver (employee+hr+tenantadmin all 403 — only manager persona holds Leave.Approve.Team). `ix_leave_pending` partial index exists as specced.

**BLOCKED (5):** TC-077 detail-panel (UI fe-platform-bound; no detail-by-id route exists), TC-085 perf (needs ~500-row load + k6; index confirmed only), TC-086 a11y / TC-087 cross-browser (fe-platform-bound), TC-088 multi-level approval (deferred — code has `TODO(multi-level)`, only single-level direct-report scope built). TC-LV-246 left draft (owned by US-LV-012, not US-LV-004).

See [[qa-personas-reseed-2026-06-25]], [[qa-no-debugger-for-perf]].
