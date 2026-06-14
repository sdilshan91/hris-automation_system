---
module: Attendance
total_user_stories: 6
total_test_cases: 92
created: 2026-06-14
updated: 2026-06-14
status: draft
---

# Attendance -- Test Matrix

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 6 (US-ATT-001, US-ATT-002, US-ATT-003, US-ATT-004, US-ATT-005, US-ATT-006) |
| Total Test Cases | 92 (83 functional/security/perf/a11y/integration + 9 dedicated multi-tenant isolation) |
| Critical Priority | 40 |
| High Priority | 52 |
| Medium Priority | 0 |
| Low Priority | 0 |
| Blocked Test Cases | 0 |
| Conditional/Deferred Test Cases | Redis cache (FR-5/US-ATT-002, FR-6/US-ATT-001): TC-ATT-001 Step 6, TC-ATT-010 Step 5, TC-ATT-013 Step 7, TC-ATT-022 Step 3, TC-ATT-023 Step 5, TC-ATT-ISO-004 -- CONDITIONAL on the Redis cache layer being wired (DB-fallback path verified meanwhile). EF-query-filter-vs-PostgreSQL-RLS (US-ATT-001 NFR-2, US-ATT-002 NFR-4, US-ATT-003 NFR-2, US-ATT-004 NFR-3): TC-ATT-ISO-001/003/005/006/007 describe the EF Core global query filter mechanism and note the RLS session-level assertion as an extension point if RLS policies are added on attendance_log / attendance_regularization. Notification dispatch (FR-4/US-ATT-003; FR-5/US-ATT-004): TC-ATT-032 (submit) and TC-ATT-037/038 (approve/reject employee notification incl. rejection reason) verify the notification SEAM now and DEFER in-app delivery/badge assertions until the Notification System (US-NTF) is built. Payroll-period lock (FR-7/US-ATT-003 submit; BR-5/US-ATT-004 approval): TC-ATT-029 (submit) and TC-ATT-045 (approval) surface the locked-period error-contract and verify the unlocked path now; the locked-period assertion is CONDITIONAL on the Payroll module. Approval workflow engine: US-ATT-003 TCs assert a workflow_instance is initiated; US-ATT-004 multi-level routing (AC-4/FR-4/BR-4, TC-ATT-044) is CONDITIONAL/DEFERRED on the Approval Workflow Engine (US-ADM-007) -- the single-level final-approval path (TC-ATT-037) and the deny-self-approval invariant (TC-ATT-042) are verified live. US-ATT-005 deferrals: Redis shift-definition cache (NFR-4, 1h TTL) -- TC-ATT-064 measures the DB-backed read path now, cache-key isolation reuses TC-ATT-ISO-004 (CONDITIONAL on Redis); EF-query-filter-vs-RLS (NFR-3) -- TC-ATT-ISO-008 describes the EF mechanism on `shift`/`employee_shift` and notes RLS as an extension point; tenant default-shift provisioning (BR-1) verified against a manually-flagged default with the Tenant-Admin auto-seed call site DEFERRED (TC-ATT-058); late-arrival flagging from grace_period (BR-4) DEFERRED on US-ATT-008 -- TC-ATT-062 verifies the shift-definition side (threshold exposed); night-shift end-to-end clock calculations integrate with US-ATT-001/002 (TC-ATT-055 verifies the definition-side cross-midnight resolution). US-ATT-006 deferrals: FR-8 HR weekly-cap ALERT notification (TC-ATT-071) DEFERS in-app/email dispatch on the Notification System (US-NTF) -- the alert SEAM (recipient=HR, tenant-scoped, references employee + weekly total) is verified now; FR-5 manager approval ROUTING via the Approval Workflow Engine (US-ADM-007) -- TC-ATT-073/077 verify the default single-level route-to-direct-manager/supervisor live, multi-level routing DEFERRED; FR-7 payroll-ready -> payroll CONSUMPTION (TC-ATT-072/074) CONDITIONAL on US-ATT-009 / Payroll (the attendance-side payroll-ready flag is set now); FR-3 public-holiday 2.5x multiplier (TC-ATT-069 Step 3) CONDITIONAL on the holiday-source integration into Attendance (weekday/weekend classification from shift working_days verified now; US-LV-007 holiday calendar is the source); NFR-2 EF-query-filter-vs-PostgreSQL-RLS (TC-ATT-ISO-009) describes the EF mechanism on `overtime_record` and notes the RLS session-level assertion as an extension point; overtime-minutes definition ambiguity (TC-ATT-067 Note) -- threshold-as-gate (60 min) vs threshold-subtracted (30 min) flagged to the caller, boundary TC-ATT-068 unaffected. |
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
| US-ATT-005 | Shift Management and Assignment per Employee | TC-ATT-051, TC-ATT-052, TC-ATT-053, TC-ATT-054, TC-ATT-055, TC-ATT-056, TC-ATT-057, TC-ATT-058, TC-ATT-059, TC-ATT-060, TC-ATT-061, TC-ATT-062, TC-ATT-063, TC-ATT-064, TC-ATT-065, TC-ATT-066 | 16 |
| Cross-cutting (ATT-005) | Multi-tenant isolation (shift + employee_shift tables) | TC-ATT-ISO-008 (+ reuses TC-ATT-ISO-001..004 for context/cache isolation) | 1 |
| US-ATT-006 | Overtime Tracking and Approval | TC-ATT-067, TC-ATT-068, TC-ATT-069, TC-ATT-070, TC-ATT-071, TC-ATT-072, TC-ATT-073, TC-ATT-074, TC-ATT-075, TC-ATT-076, TC-ATT-077, TC-ATT-078, TC-ATT-079, TC-ATT-080, TC-ATT-081, TC-ATT-082, TC-ATT-083 | 17 |
| Cross-cutting (ATT-006) | Multi-tenant isolation (overtime_record table) | TC-ATT-ISO-009 (+ reuses TC-ATT-ISO-001..004 for context/cache isolation) | 1 |

## Test Type Distribution

| Type | Test Cases | Count |
|------|------------|-------|
| Functional (ATT-001) | TC-ATT-001, TC-ATT-002, TC-ATT-003, TC-ATT-004, TC-ATT-005, TC-ATT-006, TC-ATT-007 | 7 |
| Functional (ATT-002) | TC-ATT-013, TC-ATT-014, TC-ATT-015, TC-ATT-016, TC-ATT-017, TC-ATT-018, TC-ATT-019, TC-ATT-020 | 8 |
| Functional (ATT-003) | TC-ATT-025, TC-ATT-026, TC-ATT-027, TC-ATT-028, TC-ATT-029, TC-ATT-030, TC-ATT-031, TC-ATT-033 | 8 |
| Functional (ATT-004) | TC-ATT-037, TC-ATT-038, TC-ATT-039, TC-ATT-040, TC-ATT-042, TC-ATT-043, TC-ATT-044, TC-ATT-045, TC-ATT-046 | 9 |
| Functional (ATT-005) | TC-ATT-051, TC-ATT-052, TC-ATT-053, TC-ATT-054, TC-ATT-055, TC-ATT-056, TC-ATT-057, TC-ATT-058, TC-ATT-059, TC-ATT-060, TC-ATT-061, TC-ATT-062 | 12 |
| Functional (ATT-006) | TC-ATT-067, TC-ATT-068, TC-ATT-069, TC-ATT-070, TC-ATT-071, TC-ATT-072, TC-ATT-073, TC-ATT-074, TC-ATT-075, TC-ATT-076, TC-ATT-077, TC-ATT-078, TC-ATT-079, TC-ATT-080 | 14 |
| Security (ATT-001) | TC-ATT-005 (IP allowlist), TC-ATT-008 (authz), TC-ATT-009 (authn), TC-ATT-ISO-001, TC-ATT-ISO-002, TC-ATT-ISO-003, TC-ATT-ISO-004 | 7 |
| Security (ATT-002) | TC-ATT-ISO-005 | 1 |
| Security (ATT-003) | TC-ATT-033 (audit), TC-ATT-036 (authn/authz/self-scope), TC-ATT-ISO-006 | 3 |
| Security (ATT-004) | TC-ATT-041 (authz denial), TC-ATT-042 (self-approval), TC-ATT-043 (immutability), TC-ATT-048 (audit), TC-ATT-ISO-007 | 5 |
| Security (ATT-005) | TC-ATT-052 (tenant-scoped uniqueness), TC-ATT-063 (authn/authz, HR-only), TC-ATT-ISO-008 | 3 |
| Security (ATT-006) | TC-ATT-077 (self-approval prevention), TC-ATT-078 (decision immutability), TC-ATT-080 (deterministic/auditable), TC-ATT-082 (authn/authz/self-scope/sanitisation), TC-ATT-ISO-009 | 5 |
| Integration / Concurrency (ATT-001) | TC-ATT-012 | 1 |
| Integration (ATT-002) | TC-ATT-021 (auto-clock-out Hangfire job), TC-ATT-022 (atomicity) | 2 |
| Integration (ATT-003) | TC-ATT-032 (manager notification seam -- DEFERRED on US-NTF) | 1 |
| Integration (ATT-004) | TC-ATT-047 (approval atomicity) | 1 |
| Performance (ATT-001 / ATT-002 / ATT-003 / ATT-004 / ATT-005 / ATT-006) | TC-ATT-010, TC-ATT-023, TC-ATT-034, TC-ATT-049, TC-ATT-064 (pages <2s), TC-ATT-065 (bulk assign 500 <5s), TC-ATT-081 (OT approval queue <2s) | 7 |
| Accessibility (ATT-001 / ATT-002 / ATT-003 / ATT-004 / ATT-005 / ATT-006) | TC-ATT-011, TC-ATT-024, TC-ATT-035, TC-ATT-050, TC-ATT-066, TC-ATT-083 | 6 |

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

## Acceptance Criteria Coverage (US-ATT-005)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | Create shift (name, start/end, break, working days) saved with tenant_id, available for assignment; duplicate name per tenant rejected | TC-ATT-051, TC-ATT-052 |
| AC-2 | Assign shift to one or more employees with an effective date -> employee_shift records created | TC-ATT-056 |
| AC-3 | Future-dated reassignment -- current shift active until new effective date; no overlapping active assignments | TC-ATT-057 |
| AC-4 | Delete a shift assigned to employees prevented with exact "This shift is assigned to {N} employees. Please reassign them before deleting." | TC-ATT-060 |
| AC-5 | Rotating shift -- define rotation pattern; system determines applicable shift per day across the cycle | TC-ATT-059 |

## Functional Requirement Coverage (US-ATT-005)

| FR | Covered By |
|----|------------|
| FR-1 (three types: SINGLE, ROTATING, FLEXIBLE) | TC-ATT-051 (SINGLE), TC-ATT-059 (ROTATING), TC-ATT-054 (FLEXIBLE), TC-ATT-055 (night SINGLE) |
| FR-2 (shift parameters: name, type, times, break, grace, minimum_hours, working_days) | TC-ATT-051, TC-ATT-053, TC-ATT-054, TC-ATT-062 |
| FR-3 (bulk assignment to multiple employees) | TC-ATT-056, TC-ATT-065 |
| FR-4 (effective_from/effective_to assignment history) | TC-ATT-056, TC-ATT-057 |
| FR-5 (tenant default shift for unassigned employees) | TC-ATT-058 |
| FR-6 (prevent deletion of shifts with active assignments) | TC-ATT-060 |
| FR-7 (store rotation pattern; calculate applicable shift for any date) | TC-ATT-059 |
| FR-8 (clone an existing shift to create a variant) | TC-ATT-061 |

## Non-Functional Requirement Coverage (US-ATT-005)

| NFR | Covered By |
|-----|------------|
| NFR-1 (shift management pages load < 2s P95) | TC-ATT-064 |
| NFR-2 (bulk assignment up to 500 employees < 5s) | TC-ATT-065 |
| NFR-3 (PostgreSQL RLS / tenant isolation on shift + employee_shift) | TC-ATT-ISO-008 (+ reuses TC-ATT-ISO-001..004); EF query filters today, RLS noted as extension point |
| NFR-4 (shift definitions cached in Redis, 1h TTL, invalidated on update) | TC-ATT-064 (DB-fallback), TC-ATT-ISO-004 (cache-key isolation) -- CONDITIONAL/DEFERRED on Redis |

## Business Rule Coverage (US-ATT-005)

| BR | Covered By |
|----|------------|
| BR-1 (every tenant has >= 1 default shift, created at provisioning) | TC-ATT-058 (provisioning auto-seed call site DEFERRED on Tenant Admin) |
| BR-2 (one active shift per employee at any time) | TC-ATT-057, TC-ATT-065 |
| BR-3 (assignments effective-dated; apply from effective_from) | TC-ATT-056, TC-ATT-057 |
| BR-4 (grace period defines late threshold) | TC-ATT-062 -- shift-definition side; late-flagging DEFERRED on US-ATT-008 |
| BR-6 (working_days define applicable days; non-working days not counted) | TC-ATT-062 |
| BR-7 (no zero-duration shift, start_time == end_time) | TC-ATT-053 (+ TC-ATT-055 confirms night shift end<start is valid, not zero-duration) |
| BR-8 (FLEXIBLE: only minimum_hours enforced; start/end not validated) | TC-ATT-054 |

(BR-5 break-duration auto-deduction at clock-out is owned by US-ATT-002 / TC-ATT-018; noted here as the consuming story.)

## Coverage Gaps / Notes (US-ATT-005)

- **Redis shift-definition cache (NFR-4):** not assumed wired. TC-ATT-064 verifies the DB-backed read path now; tenant-scoped cache-key isolation reuses TC-ATT-ISO-004 (CONDITIONAL on Redis). When the cache lands, assert the 1h TTL, invalidation-on-update, and cache-hit SLA. Consistent with module-wide deferred-Redis handling. **Reported to caller.**
- **PostgreSQL RLS vs EF query filters (NFR-3):** US-ATT-005 NFR-3/S10 name PostgreSQL RLS on `shift`/`employee_shift`; the platform enforces isolation via EF Core global query filters + TenantInterceptor. TC-ATT-ISO-008 (and reused TC-ATT-ISO-001/003) describe the EF mechanism and mark the RLS session-level assertion as an extension point. Consistent with US-ATT-001..004. **Reported to caller.**
- **Tenant default-shift provisioning (BR-1):** the default shift is meant to be created during tenant provisioning (Tenant Admin module). TC-ATT-058 verifies fallback resolution against a manually-flagged default; the auto-seed call site is DEFERRED on Tenant Admin. **Reported to caller.**
- **Late-arrival flagging from grace_period (BR-4):** TC-ATT-062 verifies the shift-definition side (the start_time + grace late threshold is exposed and working-day applicability is correct). The end-to-end "clock-in flagged late" assertion belongs to US-ATT-008 (grace boundary against clock-in already exercised in TC-ATT-006). **Reported to caller.**
- **Night-shift clock calculations (S10):** TC-ATT-055 verifies the definition-side cross-midnight resolution (end<start stored valid, correct work window). End-to-end clock-in/out span-midnight totals are owned by US-ATT-001/002 (TC-ATT-001/013) and integrate against seeded shift data. **Reported to caller.**

## Coverage Summary (US-ATT-005)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage (ATT-005) | 5/5 (100%) | >= 100% | PASS |
| FR Coverage (ATT-005) | 8/8 (100%) | >= 85% | PASS |
| NFR Coverage (ATT-005) | 4/4 (100%) -- NFR-3 RLS extension point; NFR-4 cache CONDITIONAL on Redis | >= 85% | PASS |
| BR Coverage (ATT-005) | 7/7 covered (BR-1..BR-4, BR-6..BR-8; BR-5 owned by US-ATT-002) -- BR-1 seed + BR-4 late-flag CONDITIONAL/DEFERRED | >= 85% | PASS |
| Multi-Tenant Isolation Tests (ATT-005) | 1 dedicated (ISO-008) + reuses ISO-001..004 + isolation aspects in TC-ATT-052/063 | >= 1 (shift + assignment) | PASS |
| Security Test Cases (ATT-005) | TC-ATT-052, TC-ATT-063, TC-ATT-ISO-008 dedicated | >= 1 | PASS |
| Performance Test Cases (ATT-005) | 2 (TC-ATT-064 pages, TC-ATT-065 bulk assign) | >= 1 | PASS |
| Accessibility Test Cases (ATT-005) | 1 (TC-ATT-066) | >= 1 | PASS |
| API Endpoint Coverage (ATT-005) | shifts CRUD + clone + assign + resolve (100%) | >= 90% | PASS |

## Acceptance Criteria Coverage (US-ATT-006)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | Clock-out beyond standard+threshold auto-creates an overtime record with excess minutes, status PENDING | TC-ATT-067 (+ TC-ATT-068 not-triggered boundary) |
| AC-2 | Pre-approval policy on -- overtime without pre-approval flagged UNAPPROVED | TC-ATT-072 |
| AC-3 | Manager overtime approval queue lists team PENDING overtime with employee/date/hours/reason | TC-ATT-073 |
| AC-4 | Manager approves -- status APPROVED and record flagged payroll-ready (approve/reject/adjust) | TC-ATT-074 (+ TC-ATT-075 adjust, TC-ATT-076 reject, TC-ATT-078 immutability) |
| AC-5 | HR monthly overtime report -- approved/pending/rejected by employee for the month | TC-ATT-079 |

## Functional Requirement Coverage (US-ATT-006)

| FR | Covered By |
|----|------------|
| FR-1 (detect overtime when total > standard + threshold) | TC-ATT-067, TC-ATT-068 |
| FR-2 (create overtime_record: employee_id, date, overtime_minutes, type, status) | TC-ATT-067, TC-ATT-072, TC-ATT-ISO-009 |
| FR-3 (tenant-configurable multiplier rates incl. weekend/holiday) | TC-ATT-069 (public-holiday 2.5x CONDITIONAL on holiday-source integration) |
| FR-4 (pre-approval workflow when tenant policy requires it) | TC-ATT-072, TC-ATT-082 |
| FR-5 (route for manager approval via the Approval Workflow Engine) | TC-ATT-073, TC-ATT-077 -- single-level default verified; multi-level routing CONDITIONAL/DEFERRED on US-ADM-007 |
| FR-6 (approve, reject, or adjust overtime hours) | TC-ATT-074, TC-ATT-075, TC-ATT-076, TC-ATT-078 |
| FR-7 (approved overtime flagged payroll-ready) | TC-ATT-074, TC-ATT-072 -- payroll CONSUMPTION CONDITIONAL on US-ATT-009/Payroll |
| FR-8 (cap daily/weekly at tenant max; alert HR if exceeded) | TC-ATT-070 (daily cap), TC-ATT-071 (weekly cap + HR-alert seam; dispatch DEFERRED on US-NTF) |

## Non-Functional Requirement Coverage (US-ATT-006)

| NFR | Covered By |
|-----|------------|
| NFR-1 (overtime detection processed in the clock-out transaction, no extra API call) | TC-ATT-067 |
| NFR-2 (PostgreSQL RLS / tenant isolation on overtime records) | TC-ATT-ISO-009 (+ reuses TC-ATT-ISO-001..004); EF query filters today, RLS noted as extension point |
| NFR-3 (overtime calc deterministic + auditable -- formula + inputs logged) | TC-ATT-080 |
| NFR-4 (overtime approval queue loads < 2s P95) | TC-ATT-081 |

## Business Rule Coverage (US-ATT-006)

| BR | Covered By |
|----|------------|
| BR-1 (overtime only when total exceeds standard + threshold) | TC-ATT-067 |
| BR-2 (threshold tenant-configurable, default 30 min; below-threshold not counted) | TC-ATT-068 |
| BR-3 (multiplier weekday 1.5x / weekend 2.0x / public holiday 2.5x) | TC-ATT-069 |
| BR-4 (max daily overtime tenant-configurable, default 4h; beyond capped + flagged) | TC-ATT-070 |
| BR-5 (max weekly overtime tenant-configurable, default 20h; alert HR) | TC-ATT-071 |
| BR-6 (overtime without pre-approval recorded UNAPPROVED, excluded from payroll) | TC-ATT-072 |
| BR-7 (rest-day/public-holiday different multiplier rates) | TC-ATT-069 |
| BR-8 (managers cannot approve their own overtime; route to supervisor/HR) | TC-ATT-077 |

## Coverage Gaps / Notes (US-ATT-006)

- **Overtime-minutes definition (TC-ATT-067):** FR-1 defines the threshold as a detection GATE ("total > standard + threshold"), which favours overtime_minutes = excess past standard (540-480 = 60 min) with the threshold only deciding whether any overtime is recognised. The task brief phrased it as 30 min (threshold subtracted from the counted minutes). TC-ATT-067 asserts 60 and flags the ambiguity; the boundary TC-ATT-068 is unaffected (no overtime under either definition at/below threshold). **Reported to caller** -- confirm against the backend overtime detector.
- **Public-holiday multiplier (FR-3/BR-3/BR-7, TC-ATT-069):** weekday/weekend classification from shift working_days is verifiable now; the public-holiday 2.5x rate requires the holiday source (US-LV-007 calendar) integrated into the Attendance overtime detector -- CONDITIONAL on that integration. **Reported to caller.**
- **Manager approval routing (FR-5):** the configurable Approval Workflow Engine (US-ADM-007) is not built. TC-ATT-073/077 verify the default single-level route to the direct manager/supervisor live; multi-level routing is DEFERRED. Consistent with US-ATT-004 TC-ATT-044 and US-LV-005 TC-LV-097. **Reported to caller.**
- **HR weekly-cap alert (FR-8, TC-ATT-071):** the Notification System (US-NTF) is not built. The HR-alert SEAM (recipient/payload/tenant-scope) is verified now; in-app/email delivery + badge assertions are DEFERRED until US-NTF. Whether the weekly cap also CAPS recorded minutes (like the daily cap) is a story ambiguity flagged in the TC. **Reported to caller.**
- **Payroll-ready -> payroll consumption (FR-7, TC-ATT-072/074):** the attendance side sets the payroll-ready flag and excludes UNAPPROVED records now; consumption by the payroll engine is US-ATT-009 / the Payroll module -- CONDITIONAL on it. **Reported to caller.**
- **PostgreSQL RLS vs EF query filters (NFR-2, TC-ATT-ISO-009):** US-ATT-006 NFR-2/S10 name PostgreSQL RLS on `overtime_record`; the platform enforces isolation via EF Core global query filters + TenantInterceptor. TC-ATT-ISO-009 (and reused TC-ATT-ISO-001..004) describe the EF mechanism and mark the RLS session-level assertion as an extension point. Consistent with US-ATT-001..005. **Reported to caller.**
- **Month/day boundary (TC-ATT-079):** detection and the monthly report use UTC day/month boundaries (per the attendance vault; tenant-timezone infra DEFERRED module-wide). If tenant-local boundaries are required, that is the same deferred concern. **Reported to caller.**

## Coverage Summary (US-ATT-006)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage (ATT-006) | 5/5 (100%) | >= 100% | PASS |
| FR Coverage (ATT-006) | 8/8 (100%) -- FR-3 holiday rate CONDITIONAL on holiday-source; FR-5 multi-level routing CONDITIONAL on US-ADM-007; FR-7 payroll consumption CONDITIONAL on US-ATT-009; FR-8 HR-alert dispatch CONDITIONAL on US-NTF | >= 85% | PASS |
| NFR Coverage (ATT-006) | 4/4 (100%) -- NFR-2 RLS noted as EF-query-filter extension point | >= 85% | PASS |
| BR Coverage (ATT-006) | 8/8 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests (ATT-006) | 1 dedicated (ISO-009) + reuses ISO-001..004 + isolation aspects in TC-ATT-073/077/082 | >= 1 (overtime read/approve/report) | PASS |
| Security Test Cases (ATT-006) | TC-ATT-077, TC-ATT-078, TC-ATT-080, TC-ATT-082, TC-ATT-ISO-009 dedicated | >= 1 | PASS |
| Performance Test Cases (ATT-006) | 1 (TC-ATT-081) | >= 1 | PASS |
| Accessibility Test Cases (ATT-006) | 1 (TC-ATT-083) | >= 1 | PASS |
| API Endpoint Coverage (ATT-006) | pre-approval + my + pending + approve + reject + report (100%) | >= 90% | PASS |

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
