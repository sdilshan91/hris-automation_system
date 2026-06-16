---
module: Performance Management
total_user_stories: 3
total_test_cases: 52
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

> US-PRF-002 (Employee Self-Rates Against Goals) is the SECOND Performance story. It adds 19 test cases: 15 functional/security/performance/accessibility (TC-PRF-002-01..15) + 4 dedicated multi-tenant isolation on the new `self_assessment` table + attachments + auto-save drafts + notifications (TC-PRF-ISO-005..008, continuing the running ISO counter from 004). All 5 acceptance criteria of US-PRF-002 are covered.
>
> KEY notes: happy path (TC-PRF-002-01) open-window My Review form shows every goal with self-rating/achievement/comment inputs -> rate all goals with valid (>=20-char) comments -> Submit -> status "Self-Assessment Submitted", weighted self-score computed, manager notified, further edits locked (AC-1/2, FR-1/2/3/4); negatives -- submit with one goal unrated rejected, partial saved as draft only (BR-2, TC-PRF-002-02), comment <20 chars / rating outside the configured scale / achievement % outside 0-100 rejected client+server (FR-2/3, TC-PRF-002-03); boundary (TC-PRF-002-04) comment exactly 20 chars / achievement 0 and 100 / rating at scale min(1) and max(5), one past each boundary rejected; save-as-draft + resume across sessions (AC-3/FR-6, TC-PRF-002-05); auto-save every 60s + crash recovery (NFR-3, TC-PRF-002-06); closed-window read-only + exact "The self-assessment period for this cycle has ended" + server block (AC-4/BR-1, TC-PRF-002-07); Hangfire deadline reminder in-app + email to non-submitters only (AC-5/FR-7, TC-PRF-002-08); authz -- employee sees only their OWN assessment via `Performance.Read.Self`, Employee A cannot view/edit Employee B's incl. IDOR by id/employeeId, unauth 401 (NFR-2, TC-PRF-002-09); file attachment limits exactly 5 files / 10MB inclusive, 6th + 10MB+1 rejected (FR-5, TC-PRF-002-10); file upload security virus-scan-before-accept + tenant-scoped storage path (NFR-4, TC-PRF-002-11); weighted self-score computation from ratings*weights with BR-4 self:manager ratio applied only at final score (FR-4, TC-PRF-002-12); submitted assessment locked unless manager/HR reopens, employee cannot self-reopen (BR-3, TC-PRF-002-13); performance form load <=400ms P95 incl. all goal data (NFR-1, TC-PRF-002-14); accessibility self-assessment UI WCAG 2.1 AA + responsive 360px, rating inputs usable via touch + keyboard (NFR-5, TC-PRF-002-15).
>
> Tenant isolation (NFR-2) for US-PRF-002: TC-PRF-ISO-005 cross-tenant read of self_assessment (Tenant B sees zero of Tenant A's records, incl. by direct ID), ISO-006 no/invalid/mismatched tenant-context rejection + cross-tenant IDOR block on the self-assessment APIs, ISO-007 cross-tenant write block + server-derived tenant_id (no body injection) + foreign goal_id/cycle_id/employee_id rejected, ISO-008 tenant-scoped attachment storage paths + auto-save drafts + submission/reminder notifications.
>
> CONDITIONAL/DEFERRED for US-PRF-002 (written as conditional, not gaps): (1) same NFR-2 RLS caveat -- the self_assessment table spec names PostgreSQL RLS; isolation is enforced via EF Core global query filters + TenantInterceptor with RLS noted as an extension point (ISO-005/007). (2) DEPENDS ON US-PRF-001 (goals assigned/acknowledged) and US-PRF-004 (appraisal cycle + self-assessment window dates) -- goals/cycle/window assumed seeded; window-state branches asserted against those dates. (3) FR-7 notification DELIVERY (manager submission notice + Hangfire deadline reminder, in-app + email) CONDITIONAL on the Notification System (S25) -- the in-app push + email enqueue are asserted (TC-PRF-002-01/-08, TC-PRF-ISO-008). (4) NFR-4 virus scanning -- if no scanner is wired today, TC-PRF-002-11 asserts the upload routes through the scan SEAM and documents it as an extension point; tenant-scoped storage paths are asserted regardless. (5) Any draft/notification cache or queue key (S10) is asserted tenant-scoped, CONDITIONAL on a cache layer -- if computed on demand it asserts tenant-filtered access with no shared/global key (TC-PRF-ISO-008). (6) NFR-1 400ms P95 requires a seeded performance environment (TC-PRF-002-14). (7) BR-4 self:manager final-score ratio is checked only insofar as it is NOT baked into the raw weighted self-score (TC-PRF-002-12); the final composite score is owned by a later Performance story (manager assessment / final calibration). (8) BR-5 (self-assessment optional when disabled in tenant config) is a precondition (self-assessment enabled) for US-PRF-002's set, not separately asserted here.

> US-PRF-003 (Manager Rates Employee Performance) is the THIRD Performance story -- it is the manager-side counterpart to US-PRF-002 and where the FINAL combined score is first computed. It adds 17 test cases: 13 functional/security/performance/accessibility (TC-PRF-003-01..13) + 4 dedicated multi-tenant isolation on the new `review` (manager-assessment) table + dashboard caches + notifications + audit (TC-PRF-ISO-009..012, continuing the running ISO counter from 008). All 5 acceptance criteria of US-PRF-003 are covered.
>
> KEY notes: happy path (TC-PRF-003-01) manager opens a direct report with submitted self-assessment -> side-by-side self vs manager view (AC-1, FR-1) -> rates all goals with >=20-char comments (FR-3) + summary <=5000 chars (FR-5) -> Submit -> weighted manager score AND final combined score computed (FR-4), status "Manager Review Submitted", employee notified, review locked (AC-2); negatives -- submit with unrated goal(s) -> error LISTING the unrated goals, blocked client+server (AC-3, TC-PRF-003-02), comment <20 chars / rating outside the configured scale / summary > 5000 rejected (FR-2/3/5, TC-PRF-003-03); FINAL-score calculation `(self*self_w)+(manager*manager_w)` data-driven across ratios 50:50, 30:70, 0:100 with server authoritative (FR-4/BR-4, TC-PRF-003-04); boundary (TC-PRF-003-05) rating at scale min(1)+max(5), comment exactly 20, summary exactly 5000, one past each rejected; Team Reviews dashboard status workflow pending self-assessment -> self-assessment submitted -> manager review pending -> completed, color-coded (AC-4, TC-PRF-003-06); scope authz -- manager can ONLY review DIRECT REPORTS, non-report -> 403 incl. IDOR, org tree authoritative, unauth 401 (BR-2/NFR-2, TC-PRF-003-07); HR `Performance.Review.All` reviews anyone + reopens a submitted review, a `.Team`-only manager cannot reopen (AC-5/BR-3, TC-PRF-003-08); submitted review locked/read-only + manager-review window enforced before/after (AC-5/BR-1, TC-PRF-003-09); optimistic concurrency -- HR + manager editing the same review, stale save -> 409, no lost update (NFR-3, TC-PRF-003-10); audit -- every rating/reopen/re-submit logged with user id + timestamp, tenant-scoped (FR-7, TC-PRF-003-11); performance -- single-employee review form (incl. self-assessment data) <=400ms P95, no N+1 (NFR-1, TC-PRF-003-12); accessibility -- WCAG 2.1 AA + keyboard-operable rating inputs + 360px stacked layout replacing side-by-side (NFR-4, TC-PRF-003-13).
>
> Tenant isolation (NFR-2) for US-PRF-003: TC-PRF-ISO-009 cross-tenant read of the review table (Tenant B sees zero of Tenant A's reviews, incl. by direct id; HR `.All` is tenant-bounded so it cannot reopen another tenant's review), ISO-010 no/invalid/mismatched tenant-context rejection + cross-tenant IDOR block on the review APIs, ISO-011 cross-tenant write block + server-derived tenant_id (no body injection) + foreign employee_id/cycle_id/goal_id rejected, ISO-012 tenant-scoped dashboard caches + submission notifications + audit entries.
>
> CONDITIONAL/DEFERRED for US-PRF-003 (written as conditional, not gaps): (1) same NFR-2 RLS caveat -- S7 names PostgreSQL RLS on the Review table; isolation is enforced via EF Core global query filters + TenantInterceptor with RLS noted as an extension point (ISO-009/011). (2) DEPENDS ON US-PRF-001 (goals), US-PRF-002 (submitted self-assessment), US-PRF-004 (cycle + manager-review window dates), and Core HR org tree (manager-report relationships) -- all assumed seeded; window-state branches asserted against those dates and scope against the org tree. (3) FR-7 notification DELIVERY (employee submission notice) CONDITIONAL on the Notification System (S25) -- in-app push asserted, email enqueue asserted, delivery conditional (TC-PRF-003-01, TC-PRF-ISO-012). (4) The dashboard/review-form cache (NFR-1, TC-PRF-003-12 / TC-PRF-ISO-012) is CONDITIONAL on a cache layer (S10) -- if computed on demand it asserts tenant-filtered queries with no shared/global key. (5) FR-7 audit logging surfaces the Audit module (S24); TC-PRF-003-11 / TC-PRF-ISO-012 assert the audit entry shape + tenant scoping against the AuditInterceptor seam. (6) NFR-1 400ms P95 requires a seeded performance environment (TC-PRF-003-12). (7) FR-6 (flag for recognition / promotion / PIP) and BR-5 (360-degree peer/report ratings folded into the final score) are NOT asserted in US-PRF-003's set -- FR-6 is a lightweight flag persisted alongside the review (touched in the happy-path summary but no dedicated TC) and BR-5 is explicitly owned by US-PRF-005 (360-degree feedback); both DEFERRED. (8) The tenant self:manager weight ratio (BR-4) is assumed settable via an HR/admin config seam (US-PRF-004-adjacent); TC-PRF-003-04 reconfigures it between runs.

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 3 (US-PRF-001, US-PRF-002, US-PRF-003) |
| Total Test Cases | 52 (40 functional/security/perf/a11y + 12 dedicated multi-tenant isolation) |
| US-PRF-001 Test Cases | 16 (TC-PRF-001-01..12 + TC-PRF-ISO-001..004) |
| US-PRF-002 Test Cases | 19 (TC-PRF-002-01..15 + TC-PRF-ISO-005..008) |
| US-PRF-003 Test Cases | 17 (TC-PRF-003-01..13 + TC-PRF-ISO-009..012) |
| Critical Priority | 20 (US-PRF-001: -01/-02/-07/-08 + ISO-001/-002/-003; US-PRF-002: -01/-07/-09 + ISO-005/-006/-007; US-PRF-003: -01/-02/-07/-09 + ISO-009/-010/-011) |
| High Priority | 32 (remaining functional/perf/a11y of all three stories + TC-PRF-ISO-004, -008, -012) |
| Medium Priority | 0 |
| Low Priority | 0 |
| Blocked Test Cases | 0 |
| Acceptance Criteria Coverage | US-PRF-001 5/5; US-PRF-002 5/5; US-PRF-003 5/5 (AC-1..AC-5 each) |
| Status | All Draft |

> Note: Critical-priority IDs total 20 across the three stories (US-PRF-001: -01/-02/-07/-08 + ISO-001/-002/-003 = 7; US-PRF-002: -01/-07/-09 + ISO-005/-006/-007 = 6; US-PRF-003: -01/-02/-07/-09 + ISO-009/-010/-011 = 7); High totals 32; summing to 52.

## User Story to Test Case Matrix

| User Story | Title | Test Cases | Count |
|------------|-------|------------|-------|
| US-PRF-001 | Manager Sets Goals/KPIs for Team Members | TC-PRF-001-01, TC-PRF-001-02, TC-PRF-001-03, TC-PRF-001-04, TC-PRF-001-05, TC-PRF-001-06, TC-PRF-001-07, TC-PRF-001-08, TC-PRF-001-09, TC-PRF-001-10, TC-PRF-001-11, TC-PRF-001-12 | 12 |
| Cross-cutting (PRF-001) | Multi-tenant isolation (goals table + caches + notifications) | TC-PRF-ISO-001, TC-PRF-ISO-002, TC-PRF-ISO-003, TC-PRF-ISO-004 | 4 |
| US-PRF-002 | Employee Self-Rates Against Goals | TC-PRF-002-01, TC-PRF-002-02, TC-PRF-002-03, TC-PRF-002-04, TC-PRF-002-05, TC-PRF-002-06, TC-PRF-002-07, TC-PRF-002-08, TC-PRF-002-09, TC-PRF-002-10, TC-PRF-002-11, TC-PRF-002-12, TC-PRF-002-13, TC-PRF-002-14, TC-PRF-002-15 | 15 |
| Cross-cutting (PRF-002) | Multi-tenant isolation (self_assessment table + attachments + auto-save + notifications) | TC-PRF-ISO-005, TC-PRF-ISO-006, TC-PRF-ISO-007, TC-PRF-ISO-008 | 4 |
| US-PRF-003 | Manager Rates Employee Performance | TC-PRF-003-01, TC-PRF-003-02, TC-PRF-003-03, TC-PRF-003-04, TC-PRF-003-05, TC-PRF-003-06, TC-PRF-003-07, TC-PRF-003-08, TC-PRF-003-09, TC-PRF-003-10, TC-PRF-003-11, TC-PRF-003-12, TC-PRF-003-13 | 13 |
| Cross-cutting (PRF-003) | Multi-tenant isolation (review table + dashboard caches + notifications + audit) | TC-PRF-ISO-009, TC-PRF-ISO-010, TC-PRF-ISO-011, TC-PRF-ISO-012 | 4 |

## Acceptance Criteria -> Test Case Coverage (US-PRF-001)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | Open-window goal-setting form with all required fields | TC-PRF-001-01, TC-PRF-001-12 |
| AC-2 | Save valid goals (100%) -> persisted tenant-scoped + linked + employee notified | TC-PRF-001-01 |
| AC-3 | Weights not summing to 100% -> "Goal weights must total 100%", submission prevented | TC-PRF-001-02 |
| AC-4 | Team goals dashboard with status (draft/submitted/acknowledged) + progress | TC-PRF-001-06, TC-PRF-001-11, TC-PRF-001-12 |
| AC-5 | Closed goal-setting window -> read-only + closed message, no modification | TC-PRF-001-08 |

## Requirement -> Test Case Coverage (US-PRF-001) (FR / BR / NFR)

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

## Acceptance Criteria -> Test Case Coverage (US-PRF-002)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | Open-window My Review form: all goals + self-rating/achievement/comment inputs | TC-PRF-002-01, TC-PRF-002-14, TC-PRF-002-15 |
| AC-2 | Rate all goals -> Submit -> "Self-Assessment Submitted" + manager notified + edits prevented | TC-PRF-002-01, TC-PRF-002-02, TC-PRF-002-13 |
| AC-3 | Save as Draft -> partial progress persisted, resume later | TC-PRF-002-05, TC-PRF-002-02, TC-PRF-002-15 |
| AC-4 | Closed window -> read-only + "The self-assessment period for this cycle has ended" | TC-PRF-002-07 |
| AC-5 | Deadline approaching -> Hangfire reminder (in-app + email) to non-submitters | TC-PRF-002-08 |

## Requirement -> Test Case Coverage (US-PRF-002) (FR / BR / NFR)

| Requirement | Covered By |
|-------------|------------|
| FR-1 display each goal (title/desc/weight/target/due) + self-rating inputs | TC-PRF-002-01, -07 |
| FR-2 self-rating uses tenant-configured scale | TC-PRF-002-03, -04 |
| FR-3 self-assessment comment min 20 chars per goal | TC-PRF-002-03, -04, -01 |
| FR-4 weighted self-assessment score from ratings + weights | TC-PRF-002-01, -12 |
| FR-5 file attachments per goal: max 5 files, 10MB each | TC-PRF-002-10, TC-PRF-ISO-008 |
| FR-6 save-as-draft persistence | TC-PRF-002-05, -06, -02 |
| FR-7 Hangfire reminder for non-submitters | TC-PRF-002-08, TC-PRF-ISO-008 |
| BR-1 submit only during the self-assessment phase window | TC-PRF-002-07 |
| BR-2 all goals must be rated before submission; partial = draft only | TC-PRF-002-02 |
| BR-3 submitted assessment locked unless manager/HR reopens | TC-PRF-002-13, TC-PRF-002-01 |
| BR-4 self:manager weight ratio applied at final score (not in raw self-score) | TC-PRF-002-12 |
| BR-5 self-assessment optional when disabled in tenant config | PRECONDITION (enabled assumed; out of US-PRF-002 set) |
| NFR-1 form loads <=400ms P95 incl. all goal data | TC-PRF-002-14 |
| NFR-2 tenant isolation (own-only + RLS / EF query filters) | TC-PRF-002-09, TC-PRF-ISO-005, -006, -007, -008 |
| NFR-3 draft auto-save every 60s | TC-PRF-002-06, TC-PRF-ISO-008 |
| NFR-4 file virus-scan + tenant-scoped storage path | TC-PRF-002-11, TC-PRF-ISO-008 |
| NFR-5 responsive 360px + touch + keyboard for rating inputs | TC-PRF-002-15 |

## Acceptance Criteria -> Test Case Coverage (US-PRF-003)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | Side-by-side view: each goal + employee self-rating/comments alongside empty manager fields | TC-PRF-003-01, TC-PRF-003-12, TC-PRF-003-13 |
| AC-2 | Rate all goals -> Submit -> manager score + final score, status "Manager Review Submitted", employee notified | TC-PRF-003-01, TC-PRF-003-03, TC-PRF-003-04 |
| AC-3 | Submit without rating all goals -> validation error LISTING unrated goals, prevented | TC-PRF-003-02 |
| AC-4 | Team Reviews dashboard: per-member status (pending self / submitted / manager pending / completed) color-coded | TC-PRF-003-06 |
| AC-5 | Submitted review read-only; editable only if HR reopens | TC-PRF-003-08, TC-PRF-003-09 |

## Requirement -> Test Case Coverage (US-PRF-003) (FR / BR / NFR)

| Requirement | Covered By |
|-------------|------------|
| FR-1 display self-rating/comments alongside each goal for manager reference | TC-PRF-003-01, -06, -09 |
| FR-2 manager rating uses the same tenant-configured scale | TC-PRF-003-03, -05 |
| FR-3 manager comment min 20 chars per goal | TC-PRF-003-01, -03, -05 |
| FR-4 final weighted score via tenant self:manager ratio | TC-PRF-003-01, -04 |
| FR-5 overall summary comment (max 5000 chars) | TC-PRF-003-01, -03, -05 |
| FR-6 flag for recognition / promotion / PIP | DEFERRED (lightweight flag; no dedicated TC) |
| FR-7 rating actions audit-logged with user id + timestamp | TC-PRF-003-11, TC-PRF-ISO-012 |
| BR-1 submit only during the manager-review phase window | TC-PRF-003-09 |
| BR-2 manager can only rate direct reports | TC-PRF-003-06, -07 |
| BR-3 HR `Performance.Review.All` rates anyone + reopens submitted reviews | TC-PRF-003-08, -09 |
| BR-4 final = `(self*self_w)+(manager*manager_w)` | TC-PRF-003-04 |
| BR-5 360-degree peer/report ratings folded into final score | DEFERRED (US-PRF-005) |
| NFR-1 single-employee review form (incl. self-assessment data) <=400ms P95 | TC-PRF-003-12 |
| NFR-2 tenant isolation (own-tenant + direct-report scope; RLS / EF query filters) | TC-PRF-003-07, TC-PRF-ISO-009, -010, -011, -012 |
| NFR-3 optimistic concurrency (HR + manager simultaneous edit) | TC-PRF-003-10 |
| NFR-4 WCAG 2.1 AA + keyboard navigation for rating inputs + 360px stacked | TC-PRF-003-13 |
