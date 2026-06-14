---
module: Attendance
total_user_stories: 1
total_test_cases: 16
created: 2026-06-14
updated: 2026-06-14
status: draft
---

# Attendance -- Test Matrix

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 1 (US-ATT-001) |
| Total Test Cases | 16 (12 functional/security/perf/a11y + 4 dedicated multi-tenant isolation) |
| Critical Priority | 9 |
| High Priority | 7 |
| Medium Priority | 0 |
| Low Priority | 0 |
| Blocked Test Cases | 0 |
| Conditional/Deferred Test Cases | TC-ATT-001 (Step 6 cache) and TC-ATT-ISO-004 -- CONDITIONAL on Redis cache layer being wired (DB-fallback path verified meanwhile); TC-ATT-010 Step 5 cache-latency -- re-measure once cache is in place; TC-ATT-ISO-001/003 -- described against EF Core global query filters (platform mechanism); extend to PostgreSQL RLS assertions if/when RLS policies are added on attendance_log (US-ATT-001 NFR-2) |
| Status | All Draft |

## User Story to Test Case Matrix

| User Story | Title | Test Cases | Count |
|------------|-------|------------|-------|
| US-ATT-001 | Employee Clock-In from Browser with Optional Geolocation | TC-ATT-001, TC-ATT-002, TC-ATT-003, TC-ATT-004, TC-ATT-005, TC-ATT-006, TC-ATT-007, TC-ATT-008, TC-ATT-009, TC-ATT-010, TC-ATT-011, TC-ATT-012 | 12 |
| Cross-cutting (ATT-001) | Multi-tenant isolation (mandatory) | TC-ATT-ISO-001, TC-ATT-ISO-002, TC-ATT-ISO-003, TC-ATT-ISO-004 | 4 |

## Test Type Distribution

| Type | Test Cases | Count |
|------|------------|-------|
| Functional (ATT-001) | TC-ATT-001, TC-ATT-002, TC-ATT-003, TC-ATT-004, TC-ATT-005, TC-ATT-006, TC-ATT-007 | 7 |
| Security (ATT-001) | TC-ATT-005 (IP allowlist), TC-ATT-008 (authz), TC-ATT-009 (authn), TC-ATT-ISO-001, TC-ATT-ISO-002, TC-ATT-ISO-003, TC-ATT-ISO-004 | 7 |
| Integration / Concurrency (ATT-001) | TC-ATT-012 | 1 |
| Performance (ATT-001) | TC-ATT-010 | 1 |
| Accessibility (ATT-001) | TC-ATT-011 | 1 |

(Note: TC-ATT-005 is counted under Functional in the AC mapping and under Security in the type distribution because it validates both a functional AC-5 flow and the network-restriction security control; TC-ATT-011 also covers cross-browser/responsive.)

## Acceptance Criteria Coverage (US-ATT-001)

| AC | Description | Covered By |
|----|-------------|------------|
| AC-1 | New attendance_log created on clock-in; tenant_id from session; UI confirmation in local time | TC-ATT-001 |
| AC-2 | Duplicate clock-in prevented with error message | TC-ATT-003, TC-ATT-012 |
| AC-3 | Geolocation required: capture if granted, block if denied | TC-ATT-004, TC-ATT-007 |
| AC-4 | Geolocation optional: clock-in proceeds without location | TC-ATT-002 |
| AC-5 | IP allowlist enforced: reject from non-allowed IP | TC-ATT-005 |

## Functional Requirement Coverage (US-ATT-001)

| FR | Covered By |
|----|------------|
| FR-1 (create attendance_log with required + nullable geo fields) | TC-ATT-001, TC-ATT-002, TC-ATT-004, TC-ATT-007 |
| FR-2 (prevent multiple active clock-ins per day, tenant tz) | TC-ATT-003, TC-ATT-012 |
| FR-3 (geo-fence radius validation) | TC-ATT-007, TC-ATT-004 |
| FR-4 (IP allowlist validation) | TC-ATT-005 |
| FR-5 (record IP + user agent for audit) | TC-ATT-001, TC-ATT-005 |
| FR-6 (update tenant-scoped Redis cache key) | TC-ATT-001, TC-ATT-ISO-004 |
| FR-7 (UTC storage, local-tz display) | TC-ATT-001, TC-ATT-006 |

## Non-Functional Requirement Coverage (US-ATT-001)

| NFR | Covered By |
|-----|------------|
| NFR-1 (clock-in P95 <= 500ms) | TC-ATT-010 |
| NFR-2 (tenant isolation on attendance_log) | TC-ATT-ISO-001, TC-ATT-ISO-002, TC-ATT-ISO-003, TC-ATT-ISO-004 |
| NFR-3 (geolocation prompt: HTTPS + consent) | TC-ATT-004, TC-ATT-009 |
| NFR-4 (idempotent within 5s; no double-submit) | TC-ATT-012 |
| NFR-5 (responsive, mobile 360px) | TC-ATT-011 |

## Business Rule Coverage (US-ATT-001)

| BR | Covered By |
|----|------------|
| BR-1 (at most one open record at a time) | TC-ATT-003, TC-ATT-012 |
| BR-2 (geolocation enforcement is tenant config) | TC-ATT-002, TC-ATT-004, TC-ATT-007 |
| BR-3 (IP allowlist is tenant config) | TC-ATT-005 |
| BR-4 (grace period: not marked late) | TC-ATT-006 |
| BR-5 (clock-in only for active employees) | TC-ATT-001 (active precondition) |
| BR-6 (selfie photo if required) | NOT YET COVERED -- see Coverage Gaps below |

## Coverage Gaps / Notes

- **BR-6 (selfie photo on clock-in):** This story lists `require_photo` as a tenant setting and a conditional `selfie_photo` input, but it is a secondary control with no dedicated acceptance criterion (AC-1..AC-5 do not mention photo). It is intentionally NOT covered by a dedicated TC in this first pass to keep the suite focused on the stated ACs. **Reported to caller** as a candidate for either a follow-up TC under this story or a dedicated photo-capture story (e.g., US-ATT-00x). Flag for the BA if photo capture is in-scope for Phase 1.
- **PostgreSQL RLS vs EF query filters:** NFR-2/S10 say "RLS"; this platform enforces tenant isolation via EF Core global query filters + TenantInterceptor today. ISO TCs describe the EF mechanism and note the RLS extension point. **Reported to caller** in case backend intends to add real RLS policies on `attendance_log`.
- **Redis cache (FR-6):** TC-ATT-001 Step 6, TC-ATT-010 Step 5, and TC-ATT-ISO-004 are written so the DB-fallback path can be verified now and the cache assertions activated once the Redis layer is wired.
- **Cross-browser:** TC-ATT-011 covers responsive + a11y across Chrome/Edge/Firefox/Safari per S10 (latest 2 versions). A dedicated standalone cross-browser functional matrix can be added if regression scope grows.

## Coverage Summary

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| FR Coverage | 7/7 (100%) | >= 85% | PASS |
| NFR Coverage | 5/5 (100%) | >= 85% | PASS |
| BR Coverage | 5/6 (83%) -- BR-6 deferred/reported | >= 85% (excl. out-of-AC BR-6) | CONDITIONAL (see gaps) |
| Multi-Tenant Isolation Tests | 4 dedicated + isolation aspects in TC-ATT-001/008/009 | >= 4 | PASS |
| Security Test Cases | 7/16 (44%) | >= 30% | PASS |
| API Endpoint Coverage | 1/1 (clock-in) (100%) | >= 90% | PASS |
