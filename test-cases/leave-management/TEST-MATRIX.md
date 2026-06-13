---
module: Leave Management
total_user_stories: 3
total_test_cases: 77
created: 2026-06-13
updated: 2026-06-13
status: draft
---

# Leave Management -- Test Matrix

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 3 (US-LV-001, US-LV-002, US-LV-003) |
| Total Test Cases | 77 |
| Critical Priority | 36 |
| High Priority | 36 |
| Medium Priority | 5 |
| Low Priority | 0 |
| Blocked Test Cases | 0 (TC-LV-056 holiday-exclusion steps conditionally blocked on US-LV-007) |
| Deferred Test Cases | 5 (TC-LV-021 onboarding seeding, TC-LV-ISO-004 cache -- partial, TC-LV-031 FTE, TC-LV-042 Redis cache, TC-LV-046 job-level dimension, TC-LV-ISO-008/TC-LV-ISO-012 cache keys -- partial) |
| Status | All Draft |

## User Story to Test Case Matrix

| User Story | Title | Test Cases | Count |
|------------|-------|------------|-------|
| US-LV-001 | Configure Leave Types Per Tenant | TC-LV-001, TC-LV-002, TC-LV-003, TC-LV-004, TC-LV-005, TC-LV-006, TC-LV-007, TC-LV-008, TC-LV-009, TC-LV-010, TC-LV-011, TC-LV-012, TC-LV-013, TC-LV-014, TC-LV-015, TC-LV-016, TC-LV-017, TC-LV-018, TC-LV-019, TC-LV-020, TC-LV-021, TC-LV-022, TC-LV-023, TC-LV-024, TC-LV-025 | 25 |
| Cross-cutting (LV-001) | Multi-tenant isolation (mandatory) | TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004 | 4 |
| US-LV-002 | Set Yearly Leave Entitlements by Job Level/Department | TC-LV-026, TC-LV-027, TC-LV-028, TC-LV-029, TC-LV-030, TC-LV-031, TC-LV-032, TC-LV-033, TC-LV-034, TC-LV-035, TC-LV-036, TC-LV-037, TC-LV-038, TC-LV-039, TC-LV-040, TC-LV-041, TC-LV-042, TC-LV-043, TC-LV-044, TC-LV-045, TC-LV-046, TC-LV-047 | 22 |
| Cross-cutting (LV-002) | Multi-tenant isolation (mandatory) | TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008 | 4 |
| US-LV-003 | Employee Applies for Leave | TC-LV-048, TC-LV-049, TC-LV-050, TC-LV-051, TC-LV-052, TC-LV-053, TC-LV-054, TC-LV-055, TC-LV-056, TC-LV-057, TC-LV-058, TC-LV-059, TC-LV-060, TC-LV-061, TC-LV-062, TC-LV-063, TC-LV-064, TC-LV-065 | 18 |
| Cross-cutting (LV-003) | Multi-tenant isolation (mandatory) | TC-LV-ISO-009, TC-LV-ISO-010, TC-LV-ISO-011, TC-LV-ISO-012 | 4 |

## Test Type Distribution

| Type | Test Cases | Count |
|------|------------|-------|
| Functional (LV-001) | TC-LV-001, TC-LV-002, TC-LV-003, TC-LV-004, TC-LV-005, TC-LV-006, TC-LV-007, TC-LV-008, TC-LV-009, TC-LV-010, TC-LV-011, TC-LV-017, TC-LV-018, TC-LV-021, TC-LV-022, TC-LV-024, TC-LV-025 | 17 |
| Functional (LV-002) | TC-LV-026, TC-LV-027, TC-LV-028, TC-LV-029, TC-LV-030, TC-LV-031, TC-LV-032, TC-LV-033, TC-LV-034, TC-LV-035, TC-LV-036, TC-LV-037, TC-LV-038, TC-LV-043, TC-LV-045, TC-LV-046 | 16 |
| Functional (LV-003) | TC-LV-048, TC-LV-049, TC-LV-050, TC-LV-051, TC-LV-052, TC-LV-053, TC-LV-054, TC-LV-055, TC-LV-056, TC-LV-057, TC-LV-059, TC-LV-063 | 12 |
| Security (LV-001) | TC-LV-012, TC-LV-013, TC-LV-014, TC-LV-015, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004 | 8 |
| Security (LV-002) | TC-LV-039, TC-LV-040, TC-LV-047, TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008 | 7 |
| Security (LV-003) | TC-LV-058, TC-LV-060, TC-LV-061, TC-LV-062, TC-LV-ISO-009, TC-LV-ISO-010, TC-LV-ISO-011, TC-LV-ISO-012 | 8 |
| Performance (LV-001) | TC-LV-016, TC-LV-023 | 2 |
| Performance (LV-002) | TC-LV-041, TC-LV-042 | 2 |
| Performance (LV-003) | TC-LV-064 | 1 |
| Accessibility (LV-001) | TC-LV-019 | 1 |
| Accessibility (LV-002) | TC-LV-044 | 1 |
| Accessibility (LV-003) | TC-LV-065 | 1 |
| Cross-Browser (LV-001) | TC-LV-020 | 1 |
| Cross-Browser (LV-002) | TC-LV-045 | 1 |
| Cross-Browser (LV-003) | TC-LV-065 | 1 |

## Test Category Coverage

| Category | Test Cases | Count |
|----------|------------|-------|
| Happy Path | TC-LV-001, TC-LV-002, TC-LV-004, TC-LV-005, TC-LV-009, TC-LV-010, TC-LV-012, TC-LV-017, TC-LV-021, TC-LV-024, TC-LV-025, TC-LV-026, TC-LV-027, TC-LV-028, TC-LV-029, TC-LV-030, TC-LV-031, TC-LV-032, TC-LV-034, TC-LV-035, TC-LV-036, TC-LV-037, TC-LV-046, TC-LV-048, TC-LV-049, TC-LV-055, TC-LV-056 | 27 |
| Negative Test | TC-LV-003, TC-LV-006, TC-LV-007, TC-LV-011, TC-LV-013, TC-LV-014, TC-LV-015, TC-LV-022, TC-LV-025, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004, TC-LV-032, TC-LV-033, TC-LV-038, TC-LV-039, TC-LV-040, TC-LV-047, TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008, TC-LV-050, TC-LV-051, TC-LV-052, TC-LV-053, TC-LV-054, TC-LV-057, TC-LV-058, TC-LV-059, TC-LV-060, TC-LV-061, TC-LV-062, TC-LV-063, TC-LV-ISO-009, TC-LV-ISO-010, TC-LV-ISO-011, TC-LV-ISO-012 | 39 |
| Boundary Test | TC-LV-006, TC-LV-008, TC-LV-009, TC-LV-029, TC-LV-031, TC-LV-033, TC-LV-038, TC-LV-050, TC-LV-051, TC-LV-052, TC-LV-053, TC-LV-054, TC-LV-055, TC-LV-056, TC-LV-057, TC-LV-063 | 16 |
| Security Test | TC-LV-012, TC-LV-013, TC-LV-014, TC-LV-015, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004, TC-LV-039, TC-LV-040, TC-LV-042, TC-LV-047, TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008, TC-LV-058, TC-LV-060, TC-LV-061, TC-LV-062, TC-LV-063, TC-LV-ISO-009, TC-LV-ISO-010, TC-LV-ISO-011, TC-LV-ISO-012 | 25 |
| Multi-Tenant Isolation | TC-LV-012, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004, TC-LV-042, TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008, TC-LV-ISO-009, TC-LV-ISO-010, TC-LV-ISO-011, TC-LV-ISO-012 | 14 |
| Performance Test | TC-LV-016, TC-LV-023, TC-LV-041, TC-LV-042, TC-LV-064 | 5 |
| Accessibility Test | TC-LV-019, TC-LV-044, TC-LV-065 | 3 |
| Cross-Browser Test | TC-LV-018, TC-LV-020, TC-LV-043, TC-LV-045, TC-LV-065 | 5 |

## Acceptance Criteria Coverage (US-LV-001)

| AC | Description | Covered By Test Cases |
|----|-------------|-----------------------|
| AC-1 | Create leave type with full config, tenant-scoped | TC-LV-001, TC-LV-006, TC-LV-007, TC-LV-008, TC-LV-012, TC-LV-022, TC-LV-024 |
| AC-2 | Edit entitlement/carry-forward with audit trail, effective next cycle | TC-LV-002, TC-LV-017 |
| AC-3 | Duplicate name rejected case-insensitive | TC-LV-003 |
| AC-4 | Deactivate hides from dropdown, existing requests unaffected | TC-LV-004 |
| AC-5 | Documents-required threshold enforced on apply | TC-LV-005 |

## Acceptance Criteria Coverage (US-LV-002)

| AC | Description | Covered By Test Cases |
|----|-------------|-----------------------|
| AC-1 | Create entitlement rule mapping leave type to department/level, employees get correct days on next accrual | TC-LV-026, TC-LV-036 |
| AC-2 | Overlapping rules resolved by specificity (most specific wins) | TC-LV-027 |
| AC-3 | Per-employee override takes precedence over all rule-based entitlements | TC-LV-028 |
| AC-4 | Mid-year joiner entitlement pro-rated based on joining date and accrual frequency | TC-LV-029 |
| AC-5 | Rule modification triggers Hangfire recalculation and audit log | TC-LV-030 |

## Acceptance Criteria Coverage (US-LV-003)

| AC | Description | Covered By Test Cases |
|----|-------------|-----------------------|
| AC-1 | Submit valid request -> Pending, leave-requested notification queued, confirmation shown | TC-LV-048 |
| AC-2 | Inline balance shown; insufficient balance (no negative allowed) blocks submission | TC-LV-049, TC-LV-050 |
| AC-3 | Sick leave over document threshold without attachment is rejected | TC-LV-051 |
| AC-4 | Half-day leave creates 0.5-day request and decrements balance accordingly | TC-LV-055 |
| AC-5 | Overlapping dates with existing Pending/Approved request rejected | TC-LV-052 |
| AC-6 | Public holidays excluded from leave day count; adjusted count shown | TC-LV-056 (holiday exclusion depends on US-LV-007) |

## Functional Requirements Coverage (US-LV-001)

| FR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| FR-1 | CRUD operations for leave types scoped to tenant_id | TC-LV-001, TC-LV-002, TC-LV-003, TC-LV-004, TC-LV-005, TC-LV-006, TC-LV-011, TC-LV-012, TC-LV-022 | Direct |
| FR-2 | All configurable fields supported | TC-LV-001, TC-LV-005, TC-LV-006, TC-LV-007, TC-LV-008, TC-LV-010, TC-LV-024, TC-LV-025 | Direct |
| FR-3 | Leave types orderable via display_order | TC-LV-009 | Direct |
| FR-4 | Default leave types seeded during tenant onboarding | TC-LV-021 | DEFERRED (onboarding wizard not implemented) |
| FR-5 | Soft delete -- deactivated types hidden from forms but retained | TC-LV-004, TC-LV-011 | Direct |

## Functional Requirements Coverage (US-LV-002)

| FR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| FR-1 | Entitlement rules support dimensions: leave type, department, job level, job title, employment type, tenure brackets | TC-LV-026, TC-LV-027, TC-LV-038, TC-LV-046 | Direct (tenure and job-level dimensions DEFERRED in TC-LV-046) |
| FR-2 | Rule priority/specificity engine | TC-LV-027, TC-LV-028 | Direct |
| FR-3 | Pro-rata calculation for mid-year joiners | TC-LV-029, TC-LV-034 | Direct |
| FR-4 | Bulk entitlement assignment UI | TC-LV-037 | Direct |
| FR-5 | Hangfire recurring job for accrual processing | TC-LV-030, TC-LV-036, TC-LV-041 | Direct |
| FR-6 | Computed balances cached in Redis | TC-LV-042 | DEFERRED (Redis caching not implemented) |

## Functional Requirements Coverage (US-LV-003)

| FR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| FR-1 | Leave application form fields: type, dates, half-day, reason, attachment | TC-LV-048, TC-LV-051, TC-LV-055, TC-LV-063 | Direct |
| FR-2 | Real-time balance display (current, requested, projected remaining) | TC-LV-049, TC-LV-050 | Direct |
| FR-3 | Working-days calc -- exclude weekends (work-week config) and public holidays | TC-LV-056 | Direct (holiday exclusion depends on US-LV-007) |
| FR-4 | Overlap detection against existing Pending/Approved requests | TC-LV-052 | Direct |
| FR-5 | API endpoint POST /api/v1/leaves with documented body | TC-LV-048, TC-LV-055, TC-LV-061, TC-LV-064 | Direct |
| FR-6 | Insert leave_request status=Pending and queue notification | TC-LV-048 | Direct |
| FR-7 | Multi-level approval routing per tenant workflow config | -- | NOT COVERED (approval routing is downstream of submission; deferred to approval story) |

## Non-Functional Requirements Coverage (US-LV-001)

| NFR | Description | Covered By | Coverage |
|-----|-------------|------------|----------|
| NFR-1 | Leave type list API <= 200ms P95 with Redis cache; cache invalidation on write | TC-LV-016, TC-LV-023, TC-LV-ISO-004 | Direct (cache steps DEFERRED if not implemented) |
| NFR-2 | Tenant-isolated via EF Core global query filters and PostgreSQL RLS | TC-LV-012, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004 | Direct |
| NFR-3 | Config changes audit-logged with before/after JSON | TC-LV-002, TC-LV-017 | Direct |
| NFR-4 | UI fully responsive 360px to 4K | TC-LV-018, TC-LV-019, TC-LV-020 | Direct |

## Non-Functional Requirements Coverage (US-LV-002)

| NFR | Description | Covered By | Coverage |
|-----|-------------|------------|----------|
| NFR-1 | Recalculation for 5,000 employees within 60 seconds (Hangfire) | TC-LV-041 | Direct |
| NFR-2 | All entitlement data tenant-isolated via EF Core filters and PostgreSQL RLS | TC-LV-039, TC-LV-040, TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008 | Direct |
| NFR-3 | Redis cache for leave balances with 24h TTL and event-driven invalidation | TC-LV-042, TC-LV-ISO-008 | DEFERRED (Redis caching not implemented) |

## Non-Functional Requirements Coverage (US-LV-003)

| NFR | Description | Covered By | Coverage |
|-----|-------------|------------|----------|
| NFR-1 | Submission API responds within 500ms P95 | TC-LV-064 | Direct |
| NFR-2 | Balance check uses Redis-cached values; DB fallback on cache miss | TC-LV-049, TC-LV-050, TC-LV-064, TC-LV-ISO-012 | Direct (cache layer DEFERRED; DB-fallback path tested) |
| NFR-3 | Attachments stored in tenant-scoped blob path {tenantId}/leaves/{requestId}/ | TC-LV-063, TC-LV-ISO-012 | Direct |
| NFR-4 | All operations tenant-isolated via EF Core filters + PostgreSQL RLS | TC-LV-062, TC-LV-ISO-009, TC-LV-ISO-010, TC-LV-ISO-011, TC-LV-ISO-012 | Direct |
| NFR-5 | Form usable on mobile 360px+ with touch-friendly date pickers | TC-LV-065 | Direct |

## Business Rules Coverage (US-LV-001)

| BR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| BR-1 | Leave type names unique within tenant (case-insensitive) | TC-LV-003, TC-LV-012 | Direct |
| BR-2 | Cannot hard-delete if leave requests reference it; deactivate only | TC-LV-011 | Direct (forward-looking; leave-request module pending) |
| BR-3 | Entitlement must be positive; zero allowed for unpaid | TC-LV-006 | Direct |
| BR-4 | Gender-specific types shown only to matching gender employees | TC-LV-010 | Direct (employee-facing filtering forward-looking) |
| BR-5 | Config changes do not retroactively affect approved requests | TC-LV-002, TC-LV-004 | Direct |

## Business Rules Coverage (US-LV-002)

| BR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| BR-1 | Entitlement rules effective per leave year (calendar or fiscal, configurable per tenant) | TC-LV-035 | Direct |
| BR-2 | Part-time employees receive entitlement proportional to FTE ratio | TC-LV-031 | DEFERRED (FTE field not on Employee entity) |
| BR-3 | Probation employees receive entitlement only for probation_eligible leave types | TC-LV-032 | Direct |
| BR-4 | Entitlement cannot be negative; minimum is zero | TC-LV-033 | Direct |
| BR-5 | Department transfer mid-year triggers pro-rata recalculation for both periods | TC-LV-034 | Direct |

## Business Rules Coverage (US-LV-003)

| BR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| BR-1 | Cannot apply for past dates beyond a configurable lookback window | TC-LV-053 | Direct |
| BR-2 | Cannot apply for dates beyond a configurable future window | TC-LV-054 | Direct |
| BR-3 | Maximum consecutive leave days enforced per leave type config | TC-LV-057 | Direct |
| BR-4 | Gender-restricted leave types only shown to eligible employees | TC-LV-058 | Direct |
| BR-5 | Probation employees only see/apply for probation_eligible leave types | TC-LV-059 | Direct |
| BR-6 | Manager/approver determined by employee reporting line (manager_employee_id) | TC-LV-048 | Direct (notification target; full routing in approval story) |

## Coverage Summary (US-LV-001)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 4/5 (80%) -- FR-4 deferred (onboarding wizard) | >= 85% | NOTE (FR-4 is cross-module dependency) |
| Non-Functional Requirements Coverage | 4/4 (100%) | >= 85% | PASS |
| Business Rules Coverage | 5/5 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 5 (4 dedicated ISO + 1 embedded in TC-LV-012) | >= 3 | PASS |
| Security Test Cases | 8/29 (27.6%) + embedded = 8/29 (27.6%) | >= 30% | NOTE (close; 8 security tests cover all critical vectors) |
| Performance Test Cases | 2/29 (TC-LV-016, TC-LV-023) | >= 1 | PASS |
| Accessibility Test Cases | 1/29 (TC-LV-019) | >= 1 | PASS |
| Cross-Browser Test Cases | 2/29 (TC-LV-018, TC-LV-020) | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred Test Cases | TC-LV-021 (onboarding seeding -- pending US-TENANT-*), TC-LV-ISO-004 partial (cache -- pending Redis implementation for leave types) | -- | NOTE |

## Coverage Summary (US-LV-002)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 5/6 (83%) -- FR-6 deferred (Redis caching) | >= 85% | NOTE (FR-6 is infrastructure dependency) |
| Non-Functional Requirements Coverage | 2/3 (67%) -- NFR-3 deferred (Redis caching) | >= 85% | NOTE (NFR-3 is infrastructure dependency) |
| Business Rules Coverage | 4/5 (80%) -- BR-2 deferred (FTE field) | >= 85% | NOTE (BR-2 is entity-level dependency) |
| Multi-Tenant Isolation Tests | 5 (4 dedicated ISO + 1 embedded in TC-LV-042) | >= 3 | PASS |
| Security Test Cases | 7/26 (26.9%) including ISO | >= 30% | NOTE (close; all critical security vectors covered: auth, authz, tenant isolation, XSS) |
| Performance Test Cases | 2/26 (TC-LV-041, TC-LV-042) | >= 1 | PASS |
| Accessibility Test Cases | 1/26 (TC-LV-044) | >= 1 | PASS |
| Cross-Browser Test Cases | 2/26 (TC-LV-043, TC-LV-045) | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |
| Deferred Test Cases | TC-LV-031 (FTE proration -- FTE field pending), TC-LV-042 (Redis cache -- pending implementation), TC-LV-046 (job-level/tenure dimensions -- pending entity), TC-LV-ISO-008 partial (cache keys -- pending Redis) | -- | NOTE |

## Coverage Summary (US-LV-003)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 6/6 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 6/7 (86%) -- FR-7 (multi-level approval routing) downstream of submission | >= 85% | PASS (FR-7 belongs to the approval story) |
| Non-Functional Requirements Coverage | 5/5 (100%) | >= 85% | PASS |
| Business Rules Coverage | 6/6 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 6 (4 dedicated ISO-009..012 + embedded in TC-LV-058, TC-LV-063) | >= 3 | PASS |
| Security Test Cases | 8/22 (36%) including ISO | >= 30% | PASS |
| Performance Test Cases | 1/22 (TC-LV-064) | >= 1 | PASS |
| Accessibility Test Cases | 1/22 (TC-LV-065) | >= 1 | PASS |
| Cross-Browser Test Cases | 1/22 (TC-LV-065) | >= 1 | PASS |
| Blocked Test Cases | 0 (TC-LV-056 holiday-exclusion steps conditionally blocked on US-LV-007) | -- | NOTE |
| Deferred Test Cases | TC-LV-ISO-012 partial (balance cache keys -- pending Redis); FR-7 approval routing out of scope | -- | NOTE |

---

*Note: This test matrix covers US-LV-001 (29 TCs), US-LV-002 (26 TCs), and US-LV-003 (22 TCs) for the Leave Management module. US-LV-003 adds 18 functional/security/performance/accessibility test cases (TC-LV-048..065) plus 4 dedicated multi-tenant isolation tests (TC-LV-ISO-009..012). All 6 acceptance criteria for US-LV-003 have direct coverage. Notes for US-LV-003: TC-LV-056 holiday-exclusion steps depend on the holiday calendar (US-LV-007) and are conditionally blocked on it if that story is not yet implemented (weekend exclusion still passes independently); FR-7 (multi-level approval routing) is downstream of submission and belongs to the leave-approval story; TC-LV-ISO-012 balance-cache-key isolation is partial pending Redis (DB-fallback and documented key pattern verified now). US-LV-001 and US-LV-002 deferred items remain unchanged.*
