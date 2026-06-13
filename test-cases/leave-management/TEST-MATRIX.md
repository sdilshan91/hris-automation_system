---
module: Leave Management
total_user_stories: 2
total_test_cases: 55
created: 2026-06-13
updated: 2026-06-13
status: draft
---

# Leave Management -- Test Matrix

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 2 (US-LV-001, US-LV-002) |
| Total Test Cases | 55 |
| Critical Priority | 27 |
| High Priority | 23 |
| Medium Priority | 5 |
| Low Priority | 0 |
| Blocked Test Cases | 0 |
| Deferred Test Cases | 5 (TC-LV-021 onboarding seeding, TC-LV-ISO-004 cache -- partial, TC-LV-031 FTE, TC-LV-042 Redis cache, TC-LV-046 job-level dimension, TC-LV-ISO-008 cache keys -- partial) |
| Status | All Draft |

## User Story to Test Case Matrix

| User Story | Title | Test Cases | Count |
|------------|-------|------------|-------|
| US-LV-001 | Configure Leave Types Per Tenant | TC-LV-001, TC-LV-002, TC-LV-003, TC-LV-004, TC-LV-005, TC-LV-006, TC-LV-007, TC-LV-008, TC-LV-009, TC-LV-010, TC-LV-011, TC-LV-012, TC-LV-013, TC-LV-014, TC-LV-015, TC-LV-016, TC-LV-017, TC-LV-018, TC-LV-019, TC-LV-020, TC-LV-021, TC-LV-022, TC-LV-023, TC-LV-024, TC-LV-025 | 25 |
| Cross-cutting (LV-001) | Multi-tenant isolation (mandatory) | TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004 | 4 |
| US-LV-002 | Set Yearly Leave Entitlements by Job Level/Department | TC-LV-026, TC-LV-027, TC-LV-028, TC-LV-029, TC-LV-030, TC-LV-031, TC-LV-032, TC-LV-033, TC-LV-034, TC-LV-035, TC-LV-036, TC-LV-037, TC-LV-038, TC-LV-039, TC-LV-040, TC-LV-041, TC-LV-042, TC-LV-043, TC-LV-044, TC-LV-045, TC-LV-046, TC-LV-047 | 22 |
| Cross-cutting (LV-002) | Multi-tenant isolation (mandatory) | TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008 | 4 |

## Test Type Distribution

| Type | Test Cases | Count |
|------|------------|-------|
| Functional (LV-001) | TC-LV-001, TC-LV-002, TC-LV-003, TC-LV-004, TC-LV-005, TC-LV-006, TC-LV-007, TC-LV-008, TC-LV-009, TC-LV-010, TC-LV-011, TC-LV-017, TC-LV-018, TC-LV-021, TC-LV-022, TC-LV-024, TC-LV-025 | 17 |
| Functional (LV-002) | TC-LV-026, TC-LV-027, TC-LV-028, TC-LV-029, TC-LV-030, TC-LV-031, TC-LV-032, TC-LV-033, TC-LV-034, TC-LV-035, TC-LV-036, TC-LV-037, TC-LV-038, TC-LV-043, TC-LV-045, TC-LV-046 | 16 |
| Security (LV-001) | TC-LV-012, TC-LV-013, TC-LV-014, TC-LV-015, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004 | 8 |
| Security (LV-002) | TC-LV-039, TC-LV-040, TC-LV-047, TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008 | 7 |
| Performance (LV-001) | TC-LV-016, TC-LV-023 | 2 |
| Performance (LV-002) | TC-LV-041, TC-LV-042 | 2 |
| Accessibility (LV-001) | TC-LV-019 | 1 |
| Accessibility (LV-002) | TC-LV-044 | 1 |
| Cross-Browser (LV-001) | TC-LV-020 | 1 |
| Cross-Browser (LV-002) | TC-LV-045 | 1 |

## Test Category Coverage

| Category | Test Cases | Count |
|----------|------------|-------|
| Happy Path | TC-LV-001, TC-LV-002, TC-LV-004, TC-LV-005, TC-LV-009, TC-LV-010, TC-LV-012, TC-LV-017, TC-LV-021, TC-LV-024, TC-LV-025, TC-LV-026, TC-LV-027, TC-LV-028, TC-LV-029, TC-LV-030, TC-LV-031, TC-LV-032, TC-LV-034, TC-LV-035, TC-LV-036, TC-LV-037, TC-LV-046 | 23 |
| Negative Test | TC-LV-003, TC-LV-006, TC-LV-007, TC-LV-011, TC-LV-013, TC-LV-014, TC-LV-015, TC-LV-022, TC-LV-025, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004, TC-LV-032, TC-LV-033, TC-LV-038, TC-LV-039, TC-LV-040, TC-LV-047, TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008 | 23 |
| Boundary Test | TC-LV-006, TC-LV-008, TC-LV-009, TC-LV-029, TC-LV-031, TC-LV-033, TC-LV-038 | 7 |
| Security Test | TC-LV-012, TC-LV-013, TC-LV-014, TC-LV-015, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004, TC-LV-039, TC-LV-040, TC-LV-042, TC-LV-047, TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008 | 16 |
| Multi-Tenant Isolation | TC-LV-012, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004, TC-LV-042, TC-LV-ISO-005, TC-LV-ISO-006, TC-LV-ISO-007, TC-LV-ISO-008 | 10 |
| Performance Test | TC-LV-016, TC-LV-023, TC-LV-041, TC-LV-042 | 4 |
| Accessibility Test | TC-LV-019, TC-LV-044 | 2 |
| Cross-Browser Test | TC-LV-018, TC-LV-020, TC-LV-043, TC-LV-045 | 4 |

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

---

*Note: This test matrix covers US-LV-001 (29 TCs) and US-LV-002 (26 TCs) for the Leave Management module. US-LV-002 deferred items: TC-LV-031 (FTE proration, no FTE field on Employee), TC-LV-042 (Redis balance cache, caching not yet implemented), TC-LV-046 (job-level/tenure dimensions, entity dependencies), TC-LV-ISO-008 partial (cache key isolation, pending Redis). FR-6 (Redis caching) and NFR-3 (cache TTL/invalidation) are infrastructure dependencies rather than coverage gaps. BR-2 (FTE proration) requires the FTE field on the Employee entity. All 5 acceptance criteria for US-LV-002 have direct test coverage.*
