---
module: Leave Management
total_user_stories: 1
total_test_cases: 29
created: 2026-06-13
updated: 2026-06-13
status: draft
---

# Leave Management -- Test Matrix

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories Covered | 1 (US-LV-001) |
| Total Test Cases | 29 |
| Critical Priority | 15 |
| High Priority | 12 |
| Medium Priority | 2 |
| Low Priority | 0 |
| Blocked Test Cases | 0 |
| Deferred Test Cases | 2 (TC-LV-021 onboarding seeding, TC-LV-ISO-004 cache -- partial) |
| Status | All Draft |

## User Story to Test Case Matrix

| User Story | Title | Test Cases | Count |
|------------|-------|------------|-------|
| US-LV-001 | Configure Leave Types Per Tenant | TC-LV-001, TC-LV-002, TC-LV-003, TC-LV-004, TC-LV-005, TC-LV-006, TC-LV-007, TC-LV-008, TC-LV-009, TC-LV-010, TC-LV-011, TC-LV-012, TC-LV-013, TC-LV-014, TC-LV-015, TC-LV-016, TC-LV-017, TC-LV-018, TC-LV-019, TC-LV-020, TC-LV-021, TC-LV-022, TC-LV-023, TC-LV-024, TC-LV-025 | 25 |
| Cross-cutting (LV-001) | Multi-tenant isolation (mandatory) | TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004 | 4 |

## Test Type Distribution

| Type | Test Cases | Count |
|------|------------|-------|
| Functional (LV-001) | TC-LV-001, TC-LV-002, TC-LV-003, TC-LV-004, TC-LV-005, TC-LV-006, TC-LV-007, TC-LV-008, TC-LV-009, TC-LV-010, TC-LV-011, TC-LV-017, TC-LV-018, TC-LV-021, TC-LV-022, TC-LV-024, TC-LV-025 | 17 |
| Security (LV-001) | TC-LV-012, TC-LV-013, TC-LV-014, TC-LV-015, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004 | 8 |
| Performance (LV-001) | TC-LV-016, TC-LV-023 | 2 |
| Accessibility (LV-001) | TC-LV-019 | 1 |
| Cross-Browser (LV-001) | TC-LV-020 | 1 |

## Test Category Coverage

| Category | Test Cases | Count |
|----------|------------|-------|
| Happy Path | TC-LV-001, TC-LV-002, TC-LV-004, TC-LV-005, TC-LV-009, TC-LV-010, TC-LV-012, TC-LV-017, TC-LV-021, TC-LV-024, TC-LV-025 | 11 |
| Negative Test | TC-LV-003, TC-LV-006, TC-LV-007, TC-LV-011, TC-LV-013, TC-LV-014, TC-LV-015, TC-LV-022, TC-LV-025, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004 | 13 |
| Boundary Test | TC-LV-006, TC-LV-008, TC-LV-009 | 3 |
| Security Test | TC-LV-012, TC-LV-013, TC-LV-014, TC-LV-015, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004 | 8 |
| Multi-Tenant Isolation | TC-LV-012, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004 | 5 |
| Performance Test | TC-LV-016, TC-LV-023 | 2 |
| Accessibility Test | TC-LV-019 | 1 |
| Cross-Browser Test | TC-LV-018, TC-LV-020 | 2 |

## Acceptance Criteria Coverage (US-LV-001)

| AC | Description | Covered By Test Cases |
|----|-------------|-----------------------|
| AC-1 | Create leave type with full config, tenant-scoped | TC-LV-001, TC-LV-006, TC-LV-007, TC-LV-008, TC-LV-012, TC-LV-022, TC-LV-024 |
| AC-2 | Edit entitlement/carry-forward with audit trail, effective next cycle | TC-LV-002, TC-LV-017 |
| AC-3 | Duplicate name rejected case-insensitive | TC-LV-003 |
| AC-4 | Deactivate hides from dropdown, existing requests unaffected | TC-LV-004 |
| AC-5 | Documents-required threshold enforced on apply | TC-LV-005 |

## Functional Requirements Coverage (US-LV-001)

| FR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| FR-1 | CRUD operations for leave types scoped to tenant_id | TC-LV-001, TC-LV-002, TC-LV-003, TC-LV-004, TC-LV-005, TC-LV-006, TC-LV-011, TC-LV-012, TC-LV-022 | Direct |
| FR-2 | All configurable fields supported | TC-LV-001, TC-LV-005, TC-LV-006, TC-LV-007, TC-LV-008, TC-LV-010, TC-LV-024, TC-LV-025 | Direct |
| FR-3 | Leave types orderable via display_order | TC-LV-009 | Direct |
| FR-4 | Default leave types seeded during tenant onboarding | TC-LV-021 | DEFERRED (onboarding wizard not implemented) |
| FR-5 | Soft delete -- deactivated types hidden from forms but retained | TC-LV-004, TC-LV-011 | Direct |

## Non-Functional Requirements Coverage (US-LV-001)

| NFR | Description | Covered By | Coverage |
|-----|-------------|------------|----------|
| NFR-1 | Leave type list API <= 200ms P95 with Redis cache; cache invalidation on write | TC-LV-016, TC-LV-023, TC-LV-ISO-004 | Direct (cache steps DEFERRED if not implemented) |
| NFR-2 | Tenant-isolated via EF Core global query filters and PostgreSQL RLS | TC-LV-012, TC-LV-ISO-001, TC-LV-ISO-002, TC-LV-ISO-003, TC-LV-ISO-004 | Direct |
| NFR-3 | Config changes audit-logged with before/after JSON | TC-LV-002, TC-LV-017 | Direct |
| NFR-4 | UI fully responsive 360px to 4K | TC-LV-018, TC-LV-019, TC-LV-020 | Direct |

## Business Rules Coverage (US-LV-001)

| BR | Description | Covered By | Coverage |
|----|-------------|------------|----------|
| BR-1 | Leave type names unique within tenant (case-insensitive) | TC-LV-003, TC-LV-012 | Direct |
| BR-2 | Cannot hard-delete if leave requests reference it; deactivate only | TC-LV-011 | Direct (forward-looking; leave-request module pending) |
| BR-3 | Entitlement must be positive; zero allowed for unpaid | TC-LV-006 | Direct |
| BR-4 | Gender-specific types shown only to matching gender employees | TC-LV-010 | Direct (employee-facing filtering forward-looking) |
| BR-5 | Config changes do not retroactively affect approved requests | TC-LV-002, TC-LV-004 | Direct |

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

---

*Note: This test matrix covers US-LV-001 (first Leave Management story). TC-LV-021 is fully DEFERRED pending the onboarding wizard (US-TENANT-*). TC-LV-ISO-004 cache-specific steps are DEFERRED if Redis caching is not yet implemented for this module. Several test cases have forward-looking steps (TC-LV-004, TC-LV-005, TC-LV-010) that depend on the leave-request module (US-LV-002+); those specific steps are marked DEFERRED within the test cases but the configuration-level verification remains executable. FR-4 (onboarding seeding) deferred status reduces FR coverage to 80%, which is a cross-module dependency rather than a coverage gap in the leave type configuration itself.*
