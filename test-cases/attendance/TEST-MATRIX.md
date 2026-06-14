---
module: Attendance
total_user_stories: 2
total_test_cases: 29
created: 2026-06-14
updated: 2026-06-14
status: draft
---

# Attendance -- Test Matrix

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 2 (US-ATT-001, US-ATT-002) |
| Total Test Cases | 29 (24 functional/security/perf/a11y/integration + 5 dedicated multi-tenant isolation) |
| Critical Priority | 14 |
| High Priority | 15 |
| Medium Priority | 0 |
| Low Priority | 0 |
| Blocked Test Cases | 0 |
| Conditional/Deferred Test Cases | Redis cache (FR-5/US-ATT-002, FR-6/US-ATT-001): TC-ATT-001 Step 6, TC-ATT-010 Step 5, TC-ATT-013 Step 7, TC-ATT-022 Step 3, TC-ATT-023 Step 5, TC-ATT-ISO-004 -- CONDITIONAL on the Redis cache layer being wired (DB-fallback path verified meanwhile). EF-query-filter-vs-PostgreSQL-RLS (US-ATT-001 NFR-2, US-ATT-002 NFR-4): TC-ATT-ISO-001/003/005 describe the EF Core global query filter mechanism and note the RLS session-level assertion as an extension point if RLS policies are added on attendance_log |
| Status | All Draft |

## User Story to Test Case Matrix

| User Story | Title | Test Cases | Count |
|------------|-------|------------|-------|
| US-ATT-001 | Employee Clock-In from Browser with Optional Geolocation | TC-ATT-001, TC-ATT-002, TC-ATT-003, TC-ATT-004, TC-ATT-005, TC-ATT-006, TC-ATT-007, TC-ATT-008, TC-ATT-009, TC-ATT-010, TC-ATT-011, TC-ATT-012 | 12 |
| Cross-cutting (ATT-001) | Multi-tenant isolation (mandatory) | TC-ATT-ISO-001, TC-ATT-ISO-002, TC-ATT-ISO-003, TC-ATT-ISO-004 | 4 |
| US-ATT-002 | Employee Clock-Out with Work Hours Auto-Calculation | TC-ATT-013, TC-ATT-014, TC-ATT-015, TC-ATT-016, TC-ATT-017, TC-ATT-018, TC-ATT-019, TC-ATT-020, TC-ATT-021, TC-ATT-022, TC-ATT-023, TC-ATT-024 | 12 |
| Cross-cutting (ATT-002) | Multi-tenant isolation (clock-out write path) | TC-ATT-ISO-005 (+ reuses TC-ATT-ISO-001..004 for table-level read/context/cache isolation) | 1 |

## Test Type Distribution

| Type | Test Cases | Count |
|------|------------|-------|
| Functional (ATT-001) | TC-ATT-001, TC-ATT-002, TC-ATT-003, TC-ATT-004, TC-ATT-005, TC-ATT-006, TC-ATT-007 | 7 |
| Functional (ATT-002) | TC-ATT-013, TC-ATT-014, TC-ATT-015, TC-ATT-016, TC-ATT-017, TC-ATT-018, TC-ATT-019, TC-ATT-020 | 8 |
| Security (ATT-001) | TC-ATT-005 (IP allowlist), TC-ATT-008 (authz), TC-ATT-009 (authn), TC-ATT-ISO-001, TC-ATT-ISO-002, TC-ATT-ISO-003, TC-ATT-ISO-004 | 7 |
| Security (ATT-002) | TC-ATT-ISO-005 | 1 |
| Integration / Concurrency (ATT-001) | TC-ATT-012 | 1 |
| Integration (ATT-002) | TC-ATT-021 (auto-clock-out Hangfire job), TC-ATT-022 (atomicity) | 2 |
| Performance (ATT-001 / ATT-002) | TC-ATT-010, TC-ATT-023 | 2 |
| Accessibility (ATT-001 / ATT-002) | TC-ATT-011, TC-ATT-024 | 2 |

(Note: TC-ATT-005 is counted under Functional in the AC mapping and under Security in the type distribution because it validates both a functional flow and a network-restriction control; TC-ATT-011 and TC-ATT-024 also cover cross-browser/responsive. TC-ATT-016/017/018/019/020 carry the boundary tag while being functionally typed.)

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

## Functional Requirement Coverage (US-ATT-002)

| FR | Covered By |
|----|------------|
| FR-1 (set clock_out to current UTC timestamp) | TC-ATT-013, TC-ATT-014, TC-ATT-015, TC-ATT-020, TC-ATT-021, TC-ATT-022, TC-ATT-ISO-005 |
| FR-2 (total_work_minutes = clock_out - clock_in, excl. break) | TC-ATT-013, TC-ATT-016, TC-ATT-017, TC-ATT-018, TC-ATT-019, TC-ATT-022, TC-ATT-023 |
| FR-3 (auto-break deduction per tenant policy) | TC-ATT-013, TC-ATT-018, TC-ATT-023 |
| FR-4 (compare to shift standard; flag overtime/short-day) | TC-ATT-016, TC-ATT-017, TC-ATT-019, TC-ATT-022, TC-ATT-023 |
| FR-5 (update tenant-scoped Redis cache key) | TC-ATT-013 (Step 7), TC-ATT-022 (Step 3), TC-ATT-023 (Step 5) -- CONDITIONAL on Redis; DB-fallback verified |
| FR-6 (capture geolocation on clock-out if required) | TC-ATT-020 |
| FR-7 (flag anomaly if span > 16h) | TC-ATT-019, TC-ATT-021 |

## Non-Functional Requirement Coverage (US-ATT-002)

| NFR | Covered By |
|-----|------------|
| NFR-1 (clock-out P95 <= 500ms) | TC-ATT-023 |
| NFR-2 (work-hours accuracy to the minute) | TC-ATT-013, TC-ATT-016, TC-ATT-017, TC-ATT-018 |
| NFR-3 (atomic; no partial updates) | TC-ATT-022 |
| NFR-4 (PostgreSQL RLS / tenant isolation on attendance_log) | TC-ATT-ISO-005 (+ TC-ATT-ISO-001..004); EF query filters today, RLS noted as extension point |
| NFR-5 (timezone display correctness; local tz) | TC-ATT-013, TC-ATT-020, TC-ATT-024 |

## Business Rule Coverage (US-ATT-002)

| BR | Covered By |
|----|------------|
| BR-1 (clock-out only with an active open record) | TC-ATT-013, TC-ATT-014, TC-ATT-015 |
| BR-2 (total = span - auto break) | TC-ATT-013, TC-ATT-016, TC-ATT-017, TC-ATT-018 |
| BR-3 (overtime when over standard + threshold, pending approval) | TC-ATT-016 |
| BR-4 (short day when under minimum) | TC-ATT-017 |
| BR-5 (end-of-day auto-clock-out job closes open records, flags regularization) | TC-ATT-021 |
| BR-6 (max 16h session; over is anomalous) | TC-ATT-019 |

## Coverage Gaps / Notes (US-ATT-002)

- **Redis cache (FR-5):** This platform's Redis cache layer is not assumed wired. TC-ATT-013 (Step 7), TC-ATT-022 (Step 3), and TC-ATT-023 (Step 5) verify the DB-fallback status path now and activate cache-specific assertions once Redis is in place. Consistent with how US-ATT-001 handled FR-6 (TC-ATT-001/010/ISO-004).
- **PostgreSQL RLS vs EF query filters (NFR-4):** US-ATT-002 NFR-4 names PostgreSQL RLS on `attendance_log`; the platform currently enforces tenant isolation via EF Core global query filters + TenantInterceptor. TC-ATT-ISO-005 (and the reused TC-ATT-ISO-001/003) describe the EF mechanism and mark the RLS session-level assertion as an extension point. **Reported to caller** in case backend adds real RLS policies.
- **Shift definitions (US-ATT-005) and overtime workflow (US-ATT-006):** TC-ATT-016/017/018/019 assume shift standard/minimum hours and break rules from US-ATT-005 and that overtime feeds US-ATT-006. These are dependencies; the TCs document the assumed shift config inline so they can run against seeded shift data and integrate once those stories land.
- **Multi-session per day:** S10 states Phase 1 is single-session; these TCs assume one clock-in/out per day. Multi-session totals are out of scope until a later phase/story.
- **Auto-clock-out flag value:** TC-ATT-021 treats the system-closed record as flagged for regularization (e.g., `status = ANOMALY` or a dedicated system-closed flag); the exact status enum value should be confirmed against the backend implementation when available.

## Coverage Summary

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage (ATT-002) | 5/5 (100%) | >= 100% | PASS |
| FR Coverage (ATT-002) | 7/7 (100%) -- FR-5 cache CONDITIONAL on Redis (DB-fallback verified) | >= 85% | PASS |
| NFR Coverage (ATT-002) | 5/5 (100%) | >= 85% | PASS |
| BR Coverage (ATT-002) | 6/6 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests (ATT-002) | 1 dedicated (ISO-005) + reuses ISO-001..004 + isolation aspects in TC-ATT-021 | >= 1 (clock-out write) | PASS |
| Security Test Cases (module) | 8/29 (28%) | >= 30% | CONDITIONAL (clock-out adds 1 dedicated ISO; read/context/cache reuse ISO-001..004) |
| API Endpoint Coverage | 2/2 (clock-in + clock-out) (100%) | >= 90% | PASS |
