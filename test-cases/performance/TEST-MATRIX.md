---
module: Performance Management
total_user_stories: 1
total_test_cases: 16
created: 2026-06-16
updated: 2026-06-16
status: in-progress
---

# Performance Management -- Test Matrix

> US-PRF-001 (Manager Sets Goals/KPIs for Team Members) is the FIRST Performance Management story and establishes `test-cases/performance/` (dir + TEST-MATRIX + the root Performance Management section in TRACEABILITY-MATRIX). It adds 16 test cases: 12 functional/security/performance/accessibility (TC-PRF-001-01..12) + 4 dedicated multi-tenant isolation on the new `goals` table (TC-PRF-ISO-001..004). The Performance module reuses the same per-story-suffix functional ID scheme as Recruitment/Payroll (TC-PRF-{NNN}-XX) with a separate running ISO counter (TC-PRF-ISO-NNN). All 5 acceptance criteria of US-PRF-001 are covered.
>
> KEY notes: happy path (TC-PRF-001-01) open-window goal form -> add goals summing to exactly 100% in 5% increments -> Save -> goals persisted tenant-scoped + linked to employee+cycle + employee notified (AC-1/2, FR-1/2/3/7); negatives -- weights summing to 95% and 105% rejected with "Goal weights must total 100%" client+server (AC-3, TC-PRF-001-02), goal count <1 or >10 (BR-2, TC-PRF-001-03), weight not in 5% increments (BR-3, TC-PRF-001-04); boundary (TC-PRF-001-05) exactly 100% / exactly 1 and exactly 10 goals / title 200 + description 2000 max length (FR-2, BR-2/3); team dashboard with per-member status + progress (AC-4, TC-PRF-001-06); authz -- only direct reporting manager or HR with Performance.SetGoal.All can set goals; non-managing manager/employee/unauth blocked (BR-4, TC-PRF-001-07); closed/not-yet-open goal-setting window read-only + "The goal-setting window for this cycle has closed" + server-side block on create/edit/delete (AC-5/BR-1, TC-PRF-001-08); input validation + XSS/SQLi sanitization + category enum + weight 1-100 + valid date (FR-2, TC-PRF-001-09); optimistic concurrency -- two sessions editing the same employee's goals, stale save -> 409, no lost update (NFR-4, TC-PRF-001-10); performance -- team goal list of <=50 members <=400ms P95 (NFR-1, TC-PRF-001-11); accessibility -- goal form + stacked weight bar + drag-reorder + team dashboard WCAG 2.1 AA across 360px-4K with keyboard-accessible reorder (NFR-3, TC-PRF-001-12).
>
> Tenant isolation (NFR-2): TC-PRF-ISO-001 cross-tenant read (Tenant B sees zero of Tenant A's goals, incl. by direct ID), ISO-002 no/invalid/mismatched tenant-context rejection + cross-tenant IDOR block, ISO-003 cross-tenant write block + server-derived tenant_id (no body injection) + foreign employee_id/cycle_id rejected, ISO-004 tenant-scoped goal-list/dashboard caches + goal-assignment notification scoping.
>
> CONDITIONAL/DEFERRED (written as conditional, not gaps): (1) NFR-2 specifies PostgreSQL RLS (`tenant_id = current_setting('app.current_tenant_id')`) on the Goals table; this platform enforces isolation via EF Core global query filters + TenantInterceptor -- TC-PRF-ISO-001/003 describe the EF mechanism and note RLS as an extension point (same caveat as Attendance/Leave/Recruitment/Payroll). (2) US-PRF-001 depends on US-PRF-004 (HR creates/manages appraisal cycles) for the active cycle + goal-setting window dates -- the cycle/window are assumed seeded; the window-state branches (open/closed/not-yet-open) are asserted against those dates. (3) FR-7 employee notification DELIVERY (in-app + email) is CONDITIONAL on the Notification System (S25) -- the enqueue/in-app push is asserted (TC-PRF-001-01, TC-PRF-ISO-004). (4) The team goal-list cache (NFR-1, TC-PRF-001-11/TC-PRF-ISO-004) is CONDITIONAL on a cache layer existing (S10) -- if computed on demand today it asserts tenant-filtered queries with no shared/global key. (5) FR-4 goal cascading (link to a departmental/org objective), FR-5 clone-from-previous-cycle / template library, FR-6 audit logging, and BR-5 acknowledged-goals-require-HR-approval-to-modify touch surfaces owned by later Performance stories / the Audit module (S24) -- they are NOT asserted in US-PRF-001's set and are deferred to those stories. (6) NFR-1 50-member <=400ms P95 SLA requires a seeded performance environment.

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 1 (US-PRF-001) |
| Total Test Cases | 16 (12 functional/security/perf/a11y + 4 dedicated multi-tenant isolation) |
| US-PRF-001 Test Cases | 16 (TC-PRF-001-01..12 + TC-PRF-ISO-001..004) |
| Critical Priority | 6 (TC-PRF-001-01, TC-PRF-001-02, TC-PRF-001-07, TC-PRF-001-08, TC-PRF-ISO-001, TC-PRF-ISO-002, TC-PRF-ISO-003) |
| High Priority | 9 (TC-PRF-001-03, -04, -05, -06, -09, -10, -11, -12, TC-PRF-ISO-004) |
| Medium Priority | 0 |
| Low Priority | 0 |
| Blocked Test Cases | 0 |
| Acceptance Criteria Coverage | US-PRF-001 5/5 (AC-1..AC-5) |
| Status | All Draft |

> Note: the Critical-priority list above totals 7 IDs (TC-PRF-001-01/-02/-07/-08 + TC-PRF-ISO-001/-002/-003); High totals 9 (the remaining functional/perf/a11y + TC-PRF-ISO-004), summing to 16.

## User Story to Test Case Matrix

| User Story | Title | Test Cases | Count |
|------------|-------|------------|-------|
| US-PRF-001 | Manager Sets Goals/KPIs for Team Members | TC-PRF-001-01, TC-PRF-001-02, TC-PRF-001-03, TC-PRF-001-04, TC-PRF-001-05, TC-PRF-001-06, TC-PRF-001-07, TC-PRF-001-08, TC-PRF-001-09, TC-PRF-001-10, TC-PRF-001-11, TC-PRF-001-12 | 12 |
| Cross-cutting (PRF-001) | Multi-tenant isolation (goals table + caches + notifications) | TC-PRF-ISO-001, TC-PRF-ISO-002, TC-PRF-ISO-003, TC-PRF-ISO-004 | 4 |

## Acceptance Criteria -> Test Case Coverage

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | Open-window goal-setting form with all required fields | TC-PRF-001-01, TC-PRF-001-12 |
| AC-2 | Save valid goals (100%) -> persisted tenant-scoped + linked + employee notified | TC-PRF-001-01 |
| AC-3 | Weights not summing to 100% -> "Goal weights must total 100%", submission prevented | TC-PRF-001-02 |
| AC-4 | Team goals dashboard with status (draft/submitted/acknowledged) + progress | TC-PRF-001-06, TC-PRF-001-11, TC-PRF-001-12 |
| AC-5 | Closed goal-setting window -> read-only + closed message, no modification | TC-PRF-001-08 |

## Requirement -> Test Case Coverage (FR / BR / NFR)

| Requirement | Covered By |
|-------------|------------|
| FR-1 create/edit/delete during window | TC-PRF-001-01, -06, -07, -08, -10 |
| FR-2 goal fields + max lengths + enum | TC-PRF-001-05, -09 |
| FR-3 weights sum to exactly 100% | TC-PRF-001-01, -02 |
| FR-4 goal cascading | DEFERRED (later Performance story) |
| FR-5 clone / template library | DEFERRED (later Performance story) |
| FR-6 audit logging | DEFERRED (Audit module S24) |
| FR-7 notify employee on assign/modify | TC-PRF-001-01, TC-PRF-ISO-004 |
| BR-1 goals only during goal-setting phase | TC-PRF-001-08 |
| BR-2 min 1 / max 10 goals per employee per cycle | TC-PRF-001-03, -05 |
| BR-3 weights in 5% increments | TC-PRF-001-04, -05 |
| BR-4 only direct manager or HR `.All` | TC-PRF-001-07 |
| BR-5 acknowledged goals need HR approval to modify | DEFERRED (later Performance story) |
| NFR-1 50-member list <=400ms P95 | TC-PRF-001-11 |
| NFR-2 tenant isolation (RLS / EF query filters) | TC-PRF-ISO-001, -002, -003, -004 |
| NFR-3 responsive 360px-4K + WCAG 2.1 AA | TC-PRF-001-12 |
| NFR-4 optimistic concurrency control | TC-PRF-001-10 |
