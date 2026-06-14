---
module: Attendance
total_user_stories: 4
total_test_cases: 57
created: 2026-06-14
updated: 2026-06-14
status: draft
---

# Attendance -- Test Matrix

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 4 (US-ATT-001, US-ATT-002, US-ATT-003, US-ATT-004) |
| Total Test Cases | 57 (50 functional/security/perf/a11y/integration + 7 dedicated multi-tenant isolation) |
| Critical Priority | 25 |
| High Priority | 32 |
| Medium Priority | 0 |
| Low Priority | 0 |
| Blocked Test Cases | 0 |
| Conditional/Deferred Test Cases | Redis cache (FR-5/US-ATT-002, FR-6/US-ATT-001): TC-ATT-001 Step 6, TC-ATT-010 Step 5, TC-ATT-013 Step 7, TC-ATT-022 Step 3, TC-ATT-023 Step 5, TC-ATT-ISO-004 -- CONDITIONAL on the Redis cache layer being wired (DB-fallback path verified meanwhile). EF-query-filter-vs-PostgreSQL-RLS (US-ATT-001 NFR-2, US-ATT-002 NFR-4, US-ATT-003 NFR-2, US-ATT-004 NFR-3): TC-ATT-ISO-001/003/005/006/007 describe the EF Core global query filter mechanism and note the RLS session-level assertion as an extension point if RLS policies are added on attendance_log / attendance_regularization. Notification dispatch (FR-4/US-ATT-003; FR-5/US-ATT-004): TC-ATT-032 (submit) and TC-ATT-037/038 (approve/reject employee notification incl. rejection reason) verify the notification SEAM now and DEFER in-app delivery/badge assertions until the Notification System (US-NTF) is built. Payroll-period lock (FR-7/US-ATT-003 submit; BR-5/US-ATT-004 approval): TC-ATT-029 (submit) and TC-ATT-045 (approval) surface the locked-period error-contract and verify the unlocked path now; the locked-period assertion is CONDITIONAL on the Payroll module. Approval workflow engine: US-ATT-003 TCs assert a workflow_instance is initiated; US-ATT-004 multi-level routing (AC-4/FR-4/BR-4, TC-ATT-044) is CONDITIONAL/DEFERRED on the Approval Workflow Engine (US-ADM-007) -- the single-level final-approval path (TC-ATT-037) and the deny-self-approval invariant (TC-ATT-042) are verified live. |
| Status | All Draft |

## User Story to Test Case Matrix

| User Story | Title | Test Cases | Count |
|------------|-------|------------|-------|
| US-ATT-001 | Employee Clock-In from Browser with Optional Geolocation | TC-ATT-001, TC-ATT-002, TC-ATT-003, TC-ATT-004, TC-ATT-005, TC-ATT-006, TC-ATT-007, TC-ATT-008, TC-ATT-009, TC-ATT-010, TC-ATT-011, TC-ATT-012 | 12 |
| Cross-cutting (ATT-001) | Multi-tenant isolation (mandatory) | TC-ATT-ISO-001, TC-ATT-ISO-002, TC-ATT-ISO-003, TC-ATT-ISO-004 | 4 |
| US-ATT-002 | Employee Clock-Out with Work Hours Auto-Calculation | TC-ATT-013, TC-ATT-014, TC-ATT-015, TC-ATT-016, TC-ATT-017, TC-ATT-018, TC-ATT-019, TC-ATT-020, TC-ATT-021, TC-ATT-022, TC-ATT-023, TC-ATT-024 | 12 |
| Cross-cutting (ATT-002) | Multi-tenant isolation (clock-out write path) | TC-ATT-ISO-005 (+ reuses TC-ATT-ISO-001..004 for table-level read/context/cache isolation) | 1 |
| US-ATT-003 | Attendance Regularization Request (Forgot Clock-In/Out) | TC-ATT-025, TC-ATT-026, TC-ATT-027, TC-ATT-028, TC-ATT-029, TC-ATT-030, TC-ATT-031, TC-ATT-032, TC-ATT-033, TC-ATT-034, TC-ATT-035, TC-ATT-036 | 12 |
| Cross-cutting (ATT-003) | Multi-tenant isolation (regularization read + submit path) | TC-ATT-ISO-006 (+ reuses TC-ATT-ISO-001..004 for table-level read/context/cache isolation) | 1 |
| US-ATT-004 | Manager Approves/Rejects Regularization Requests | TC-ATT-037, TC-ATT-038, TC-ATT-039, TC-ATT-040, TC-ATT-041, TC-ATT-042, TC-ATT-043, TC-ATT-044, TC-ATT-045, TC-ATT-046, TC-ATT-047, TC-ATT-048, TC-ATT-049, TC-ATT-050 | 14 |
| Cross-cutting (ATT-004) | Multi-tenant isolation (approve/reject mutation path) | TC-ATT-ISO-007 (+ reuses TC-ATT-ISO-001..004, TC-ATT-ISO-006 for table-level read/context/cache isolation) | 1 |

## Test Type Distribution

| Type | Test Cases | Count |
|------|------------|-------|
| Functional (ATT-001) | TC-ATT-001, TC-ATT-002, TC-ATT-003, TC-ATT-004, TC-ATT-005, TC-ATT-006, TC-ATT-007 | 7 |
| Functional (ATT-002) | TC-ATT-013, TC-ATT-014, TC-ATT-015, TC-ATT-016, TC-ATT-017, TC-ATT-018, TC-ATT-019, TC-ATT-020 | 8 |
| Functional (ATT-003) | TC-ATT-025, TC-ATT-026, TC-ATT-027, TC-ATT-028, TC-ATT-029, TC-ATT-030, TC-ATT-031, TC-ATT-033 | 8 |
| Functional (ATT-004) | TC-ATT-037, TC-ATT-038, TC-ATT-039, TC-ATT-040, TC-ATT-042, TC-ATT-043, TC-ATT-044, TC-ATT-045, TC-ATT-046 | 9 |
| Security (ATT-001) | TC-ATT-005 (IP allowlist), TC-ATT-008 (authz), TC-ATT-009 (authn), TC-ATT-ISO-001, TC-ATT-ISO-002, TC-ATT-ISO-003, TC-ATT-ISO-004 | 7 |
| Security (ATT-002) | TC-ATT-ISO-005 | 1 |
| Security (ATT-003) | TC-ATT-033 (audit), TC-ATT-036 (authn/authz/self-scope), TC-ATT-ISO-006 | 3 |
| Security (ATT-004) | TC-ATT-041 (authz denial), TC-ATT-042 (self-approval), TC-ATT-043 (immutability), TC-ATT-048 (audit), TC-ATT-ISO-007 | 5 |
| Integration / Concurrency (ATT-001) | TC-ATT-012 | 1 |
| Integration (ATT-002) | TC-ATT-021 (auto-clock-out Hangfire job), TC-ATT-022 (atomicity) | 2 |
| Integration (ATT-003) | TC-ATT-032 (manager notification seam -- DEFERRED on US-NTF) | 1 |
| Integration (ATT-004) | TC-ATT-047 (approval atomicity) | 1 |
| Performance (ATT-001 / ATT-002 / ATT-003 / ATT-004) | TC-ATT-010, TC-ATT-023, TC-ATT-034, TC-ATT-049 | 4 |
| Accessibility (ATT-001 / ATT-002 / ATT-003 / ATT-004) | TC-ATT-011, TC-ATT-024, TC-ATT-035, TC-ATT-050 | 4 |

(Note: TC-ATT-005 is counted under Functional in the AC mapping and under Security in the type distribution because it validates both a functional flow and a network-restriction control; TC-ATT-011/024/035 also cover cross-browser/responsive. TC-ATT-016/017/018/019/020 and TC-ATT-031 carry the boundary tag while being functionally typed. TC-ATT-033 is typed security/audit but verifies a functional submit side effect.)

## Acceptance Criteria Coverage (US-ATT-001)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | New attendance_log created on clock-in; tenant_id from session; UI confirmation in local time | TC-ATT-001 |
| AC-2 | Duplicate clock-in prevented with error message | TC-ATT-003, TC-ATT-012 |
| AC-3 | Geolocation required: capture if granted, block if denied | TC-ATT-004, TC-ATT-007 |
| AC-4 | Geolocation optional: clock-in proceeds without location | TC-ATT-002 |
| AC-5 | IP allowlist enforced: reject from non-allowed IP | TC-ATT-005 |

## Acceptance Criteria Coverage (US-ATT-002)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | Clock-out sets clock_out (UTC); total work hours calculated and displayed | TC-ATT-013 |
| AC-2 | No open clock-in record -> clear error message | TC-ATT-014, TC-ATT-015 |
| AC-3 | Hours over shift standard flagged as overtime, stored separately | TC-ATT-016 |
| AC-4 | Hours below shift minimum flagged as "short day" for HR review | TC-ATT-017 |
| AC-5 | Tenant geo policy on clock-out: capture lat/lon if permitted | TC-ATT-020 |

## Acceptance Criteria Coverage (US-ATT-003)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | Missed clock-in (no record) -> PENDING regularization + workflow initiated | TC-ATT-025 |
| AC-2 | Clocked in but forgot clock-out -> PENDING regularization linked to existing attendance_log | TC-ATT-026 |
| AC-3 | Date older than lookback -> reject with exact "...the last {N} days." message | TC-ATT-027, TC-ATT-031 |
| AC-4 | Duplicate pending for same date -> reject with exact "A pending regularization request already exists for this date." | TC-ATT-028 |
| AC-5 | Date in a locked payroll period -> reject with exact "This date falls within a locked payroll period. Please contact HR." | TC-ATT-029 |

## Functional Requirement Coverage (US-ATT-003)

| FR | Covered By |
|----|------------|
| FR-1 (regularization form: date, type, corrected time(s), reason) | TC-ATT-025, TC-ATT-026, TC-ATT-030, TC-ATT-035 |
| FR-2 (create attendance_regularization with required fields; tenant/employee from session) | TC-ATT-025, TC-ATT-026, TC-ATT-033, TC-ATT-034, TC-ATT-036, TC-ATT-ISO-006 |
| FR-3 (initiate tenant's configured approval workflow on submit) | TC-ATT-025, TC-ATT-026 (workflow_instance_id asserted; multi-level/approve-reject -> US-ATT-004) |
| FR-4 (in-app notification to approver/line manager) | TC-ATT-032 -- CONDITIONAL/DEFERRED on US-NTF (seam verified now) |
| FR-5 (validate times: clock-in before clock-out, single day, not future) | TC-ATT-030 |
| FR-6 (tenant-configurable lookback period, default 7 days) | TC-ATT-027, TC-ATT-031, TC-ATT-034 |
| FR-7 (prevent regularization within a locked payroll period) | TC-ATT-029 -- locked-period assertion CONDITIONAL on Payroll module (unlocked path verified) |

## Non-Functional Requirement Coverage (US-ATT-003)

| NFR | Covered By |
|-----|------------|
| NFR-1 (submission P95 <= 500ms) | TC-ATT-034 |
| NFR-2 (PostgreSQL RLS / tenant isolation on attendance_regularization) | TC-ATT-ISO-006 (+ reuses TC-ATT-ISO-001..004); EF query filters today, RLS noted as extension point |
| NFR-3 (all regularization actions recorded in audit log) | TC-ATT-033 (submit action; approve/reject -> US-ATT-004) |
| NFR-4 (accessible & responsive, 360px minimum) | TC-ATT-035 |

## Business Rule Coverage (US-ATT-003)

| BR | Covered By |
|----|------------|
| BR-1 (requires >= 1 level of approval -- workflow/approver) | TC-ATT-025, TC-ATT-026, TC-ATT-032 |
| BR-2 (lookback tenant-configurable, default 7 days) | TC-ATT-027, TC-ATT-031 |
| BR-3 (only one pending regularization per employee per date) | TC-ATT-028 |
| BR-4 (no regularization for future dates) | TC-ATT-030 |
| BR-5 (link to existing attendance_log if present; new log on approval) | TC-ATT-025 (null link, no log created), TC-ATT-026 (linked to existing log) |
| BR-6 (no regularization in locked payroll period unless HR unlocks) | TC-ATT-029 |
| BR-7 (reason mandatory, >= 10 characters) | TC-ATT-030 |

## Coverage Gaps / Notes (US-ATT-003)

- **Notification dispatch (FR-4):** The Notification System (US-NTF) is not yet built. TC-ATT-032 verifies the submit-time notification SEAM (correct recipient = line manager, tenant-scoped, payload references the regularization_id) now, and DEFERS the end-to-end in-app delivery + badge-count assertions until US-NTF lands. Consistent with how leave-management notification dispatch was treated as a log-only/queued seam DEFERRED on the notifications module. **Reported to caller.**
- **Payroll-period lock (FR-7/BR-6):** The locked-period rejection (TC-ATT-029) depends on the Payroll module exposing period-lock state. The unlocked/no-lock path and the exact error message contract are verified now; the locked-period assertion is CONDITIONAL on Payroll and re-run once it lands. **Reported to caller.**
- **Approval Workflow Engine (FR-3/BR-1):** TC-ATT-025/026 assert that a `workflow_instance_id` is initiated on submit. Multi-level routing configuration and the approve/reject side belong to US-ATT-004 and the workflow-config story; this story covers only initiation. **Reported to caller** as a dependency.
- **PostgreSQL RLS vs EF query filters (NFR-2):** US-ATT-003 NFR-2 names PostgreSQL RLS on `attendance_regularization`; the platform currently enforces tenant isolation via EF Core global query filters + TenantInterceptor. TC-ATT-ISO-006 (and the reused TC-ATT-ISO-001/003) describe the EF mechanism and mark the RLS session-level assertion as an extension point. **Reported to caller** in case backend adds real RLS policies. Consistent with US-ATT-001/002.
- **attendance_log creation deferred to approval (S10/BR-5):** TC-ATT-025 asserts NO attendance_log is created at submission for MISSED_BOTH; TC-ATT-026 asserts the existing log is unmodified until approval. The log create/update side is verified under US-ATT-004.

## Acceptance Criteria Coverage (US-ATT-004)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | Approve -> status APPROVED, attendance_log created/updated with regularized times, employee notified | TC-ATT-037 (+ TC-ATT-047 atomicity, TC-ATT-044 final-approval log write) |
| AC-2 | Reject with mandatory reason -> status REJECTED, employee notified with reason | TC-ATT-038 |
| AC-3 | Approval queue lists pending requests for direct reports (employee, date, times, reason, submitted-on) | TC-ATT-040 (+ TC-ATT-049 perf, TC-ATT-050 a11y) |
| AC-4 | Multi-level workflow -- level-1 approval keeps status PENDING until final level | TC-ATT-044 -- CONDITIONAL/DEFERRED on the Workflow Engine (US-ADM-007) |
| AC-5 | Approve for a non-team employee -> denied with exact "You are not authorized to approve requests for this employee." | TC-ATT-041 |

## Functional Requirement Coverage (US-ATT-004)

| FR | Covered By |
|----|------------|
| FR-1 (filterable list of pending requests for the manager's team) | TC-ATT-040, TC-ATT-049 |
| FR-2 (on approval, create/update attendance_log with regularized times, recalc total_work_minutes) | TC-ATT-037, TC-ATT-044, TC-ATT-046, TC-ATT-047 |
| FR-3 (on rejection, require reason min 10 chars, store in workflow history) | TC-ATT-038, TC-ATT-039 |
| FR-4 (advance workflow per the tenant's configured approval chain) | TC-ATT-044 -- CONDITIONAL/DEFERRED on US-ADM-007 (single-level default verified via TC-ATT-037/042) |
| FR-5 (notify the employee on approval/rejection) | TC-ATT-037, TC-ATT-038 -- CONDITIONAL/DEFERRED on US-NTF (dispatch seam, incl. rejection reason, verified now) |
| FR-6 (log approval/rejection in audit log -- manager id, timestamp, comment) | TC-ATT-048 (+ TC-ATT-037/038 step) |
| FR-7 (manager may only approve requests for direct reports) | TC-ATT-041, TC-ATT-046, TC-ATT-ISO-007 |
| FR-8 (update Redis cache for the affected employee's daily attendance status on approval) | TC-ATT-037 (DB-fallback path) -- CONDITIONAL/DEFERRED on the Redis cache layer |

## Non-Functional Requirement Coverage (US-ATT-004)

| NFR | Covered By |
|-----|------------|
| NFR-1 (approval queue loads < 2s P95 for up to 50 pending requests) | TC-ATT-049 |
| NFR-2 (approval/rejection atomic -- both update or neither) | TC-ATT-047 (+ TC-ATT-037) |
| NFR-3 (tenant isolation -- managers only see requests within their tenant) | TC-ATT-ISO-007 (+ reuses TC-ATT-ISO-001..004, TC-ATT-ISO-006); EF query filters today, RLS noted as extension point |
| NFR-4 (approval actions immutable in the audit log) | TC-ATT-043, TC-ATT-048 |

## Business Rule Coverage (US-ATT-004)

| BR | Covered By |
|----|------------|
| BR-1 (rejection reason mandatory, min 10 chars) | TC-ATT-038, TC-ATT-039 |
| BR-2 (approval comment optional) | TC-ATT-037, TC-ATT-039 (positive control) |
| BR-3 (decision immutable once approved/rejected) | TC-ATT-043 (+ TC-ATT-046 mixed-batch) |
| BR-4 (attendance_log updated only on final approval) | TC-ATT-044 -- CONDITIONAL/DEFERRED on US-ADM-007 (single-level final write via TC-ATT-037) |
| BR-5 (approval blocked if date in a locked payroll period -- contact HR) | TC-ATT-045 -- CONDITIONAL on the Payroll module (unlocked path verified) |
| BR-6 (managers cannot approve their own requests; route to supervisor/HR) | TC-ATT-042 |
| BR-7 (bulk approval -- select multiple, approve in one action) | TC-ATT-046 |

## Coverage Gaps / Notes (US-ATT-004)

- **Multi-level approval workflow (AC-4/FR-4/BR-4):** the Approval Workflow Engine (US-ADM-007) that drives configurable N-level chains is not built. TC-ATT-044 records the multi-level routing + final-only attendance_log write as CONDITIONAL/DEFERRED; the single-level final-approval path (TC-ATT-037) and the deny-self-approval invariant (TC-ATT-042) are verified live. Consistent with US-LV-005 TC-LV-097 and US-ATT-003. **Reported to caller.**
- **Employee notification (FR-5):** the Notification System (US-NTF) is not built. TC-ATT-037 (approve) and TC-ATT-038 (reject, incl. rejection reason in payload) verify the dispatch SEAM (recipient = the requesting employee, tenant-scoped, payload references regularization_id + outcome) now and DEFER end-to-end in-app delivery/badge assertions until US-NTF lands. Consistent with US-ATT-003 TC-ATT-032. **Reported to caller.**
- **Redis status cache (FR-8):** not assumed wired. TC-ATT-037 verifies the DB-fallback daily-status path now; the cache-update-on-approval assertion is CONDITIONAL on the Redis layer. Consistent with US-ATT-001 FR-6 / US-ATT-002 FR-5. **Reported to caller.**
- **Payroll-period lock at approval (BR-5):** depends on the Payroll module exposing period-lock state. TC-ATT-045 verifies the unlocked-approval path and the contact-HR error-contract now; the locked-period block is CONDITIONAL on Payroll. Mirrors US-ATT-003 TC-ATT-029 (submit-time lock). **Reported to caller.**
- **PostgreSQL RLS vs EF query filters (NFR-3):** US-ATT-004 NFR-3/S10 name PostgreSQL RLS; the platform enforces tenant isolation via EF Core global query filters + TenantInterceptor. TC-ATT-ISO-007 (and reused TC-ATT-ISO-001/003/006) describe the EF mechanism and mark the RLS session-level assertion as an extension point. Consistent with US-ATT-001/002/003. **Reported to caller.**
- **attendance_log create/update on approval (FR-2/BR-4):** TC-ATT-037 covers the CREATE branch (MISSED_BOTH, no prior log); the UPDATE branch (MISSED_CLOCK_OUT linked to an existing log) uses the same mechanism with total_work_minutes recalculation asserted in both. This is the approval-side counterpart to US-ATT-003's submit-side deferral (TC-ATT-025/026, which assert NO log mutation at submission).

## Coverage Summary (US-ATT-004)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage (ATT-004) | 5/5 (100%) -- AC-4 multi-level CONDITIONAL on US-ADM-007 (single-level verified) | >= 100% | PASS |
| FR Coverage (ATT-004) | 8/8 (100%) -- FR-4 workflow CONDITIONAL on US-ADM-007; FR-5 notification CONDITIONAL on US-NTF; FR-8 cache CONDITIONAL on Redis | >= 85% | PASS |
| NFR Coverage (ATT-004) | 4/4 (100%) -- NFR-3 RLS noted as EF-query-filter extension point | >= 85% | PASS |
| BR Coverage (ATT-004) | 7/7 (100%) -- BR-4 final-write CONDITIONAL on US-ADM-007; BR-5 locked-period CONDITIONAL on Payroll | >= 85% | PASS |
| Multi-Tenant Isolation Tests (ATT-004) | 1 dedicated (ISO-007) + reuses ISO-001..004, ISO-006 + isolation aspects in TC-ATT-041/048 | >= 1 (approve/reject mutation) | PASS |
| Security Test Cases (ATT-004) | TC-ATT-041, TC-ATT-042, TC-ATT-043, TC-ATT-048, TC-ATT-ISO-007 dedicated | >= 1 | PASS |
| Performance Test Cases (ATT-004) | 1 (TC-ATT-049) | >= 1 | PASS |
| Accessibility Test Cases (ATT-004) | 1 (TC-ATT-050) | >= 1 | PASS |
| API Endpoint Coverage (ATT-004) | approve + reject + bulk-approve + approval-queue (100%) | >= 90% | PASS |

## Coverage Summary

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage (ATT-003) | 5/5 (100%) | >= 100% | PASS |
| FR Coverage (ATT-003) | 7/7 (100%) -- FR-4 notification CONDITIONAL on US-NTF; FR-7 locked-period CONDITIONAL on Payroll | >= 85% | PASS |
| NFR Coverage (ATT-003) | 4/4 (100%) | >= 85% | PASS |
| BR Coverage (ATT-003) | 7/7 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests (ATT-003) | 1 dedicated (ISO-006) + reuses ISO-001..004 + isolation aspects in TC-ATT-033/036 | >= 1 (regularization submit) | PASS |
| Security Test Cases (module) | 11/42 (26%) | >= 30% | CONDITIONAL (regularization adds TC-ATT-033/036/ISO-006; read/context/cache reuse ISO-001..004) |
| API Endpoint Coverage | 3/3 (clock-in + clock-out + regularization submit) (100%) | >= 90% | PASS |
