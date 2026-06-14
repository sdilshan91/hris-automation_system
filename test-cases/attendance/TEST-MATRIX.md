---
module: Attendance
total_user_stories: 3
total_test_cases: 42
created: 2026-06-14
updated: 2026-06-14
status: draft
---

# Attendance -- Test Matrix

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 3 (US-ATT-001, US-ATT-002, US-ATT-003) |
| Total Test Cases | 42 (36 functional/security/perf/a11y/integration + 6 dedicated multi-tenant isolation) |
| Critical Priority | 19 |
| High Priority | 23 |
| Medium Priority | 0 |
| Low Priority | 0 |
| Blocked Test Cases | 0 |
| Conditional/Deferred Test Cases | Redis cache (FR-5/US-ATT-002, FR-6/US-ATT-001): TC-ATT-001 Step 6, TC-ATT-010 Step 5, TC-ATT-013 Step 7, TC-ATT-022 Step 3, TC-ATT-023 Step 5, TC-ATT-ISO-004 -- CONDITIONAL on the Redis cache layer being wired (DB-fallback path verified meanwhile). EF-query-filter-vs-PostgreSQL-RLS (US-ATT-001 NFR-2, US-ATT-002 NFR-4, US-ATT-003 NFR-2): TC-ATT-ISO-001/003/005/006 describe the EF Core global query filter mechanism and note the RLS session-level assertion as an extension point if RLS policies are added on attendance_log / attendance_regularization. Notification dispatch (FR-4/US-ATT-003): TC-ATT-032 verifies the manager-notification SEAM now and DEFERS in-app delivery/badge assertions until the Notification System (US-NTF) is built. Payroll-period lock (FR-7/US-ATT-003): TC-ATT-029 surfaces the locked-period error-contract and verifies the unlocked path now; the locked-period assertion is CONDITIONAL on the Payroll module. Approval workflow engine (FR-3/US-ATT-003): TCs assert a workflow_instance is initiated; multi-level routing and the approve/reject side (US-ATT-004) are out of this story's scope. |
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

## Test Type Distribution

| Type | Test Cases | Count |
|------|------------|-------|
| Functional (ATT-001) | TC-ATT-001, TC-ATT-002, TC-ATT-003, TC-ATT-004, TC-ATT-005, TC-ATT-006, TC-ATT-007 | 7 |
| Functional (ATT-002) | TC-ATT-013, TC-ATT-014, TC-ATT-015, TC-ATT-016, TC-ATT-017, TC-ATT-018, TC-ATT-019, TC-ATT-020 | 8 |
| Functional (ATT-003) | TC-ATT-025, TC-ATT-026, TC-ATT-027, TC-ATT-028, TC-ATT-029, TC-ATT-030, TC-ATT-031, TC-ATT-033 | 8 |
| Security (ATT-001) | TC-ATT-005 (IP allowlist), TC-ATT-008 (authz), TC-ATT-009 (authn), TC-ATT-ISO-001, TC-ATT-ISO-002, TC-ATT-ISO-003, TC-ATT-ISO-004 | 7 |
| Security (ATT-002) | TC-ATT-ISO-005 | 1 |
| Security (ATT-003) | TC-ATT-033 (audit), TC-ATT-036 (authn/authz/self-scope), TC-ATT-ISO-006 | 3 |
| Integration / Concurrency (ATT-001) | TC-ATT-012 | 1 |
| Integration (ATT-002) | TC-ATT-021 (auto-clock-out Hangfire job), TC-ATT-022 (atomicity) | 2 |
| Integration (ATT-003) | TC-ATT-032 (manager notification seam -- DEFERRED on US-NTF) | 1 |
| Performance (ATT-001 / ATT-002 / ATT-003) | TC-ATT-010, TC-ATT-023, TC-ATT-034 | 3 |
| Accessibility (ATT-001 / ATT-002 / ATT-003) | TC-ATT-011, TC-ATT-024, TC-ATT-035 | 3 |

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
