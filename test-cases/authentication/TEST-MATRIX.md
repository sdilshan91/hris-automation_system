---
module: Authentication & Authorization
total_user_stories: 10
total_test_cases: 43
created: 2026-05-11
status: draft
---

# Authentication & Authorization -- Test Matrix

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories | 10 |
| Total Test Cases | 38 (functional/security/perf/a11y) + 4 (isolation) + 1 (reserved MFA block 029-038) = 43 |
| Critical Priority | 29 |
| High Priority | 9 |
| Medium Priority | 1 |
| Low Priority | 0 |
| Status | All Draft |

## User Story to Test Case Matrix

| User Story | Title | Test Cases | Count |
|------------|-------|------------|-------|
| US-AUTH-001 | Admin login with username and password | TC-AUTH-001, TC-AUTH-002, TC-AUTH-003, TC-AUTH-004 | 4 |
| US-AUTH-002 | JWT token issuance and refresh token flow | TC-AUTH-005, TC-AUTH-006, TC-AUTH-007 | 3 |
| US-AUTH-003 | User logout and token invalidation | TC-AUTH-008, TC-AUTH-009 | 2 |
| US-AUTH-004 | Password reset flow | TC-AUTH-010, TC-AUTH-011, TC-AUTH-012 | 3 |
| US-AUTH-005 | Multi-factor authentication (TOTP) | TC-AUTH-013, TC-AUTH-014, TC-AUTH-015 | 3 |
| US-AUTH-006 | Role-based access control (RBAC) | TC-AUTH-016, TC-AUTH-017, TC-AUTH-018, TC-AUTH-039, TC-AUTH-040, TC-AUTH-041, TC-AUTH-042, TC-AUTH-043, TC-AUTH-044, TC-AUTH-045, TC-AUTH-046, TC-AUTH-047, TC-AUTH-048, TC-AUTH-049, TC-AUTH-050 | 15 |
| US-AUTH-007 | Tenant resolution from subdomain | TC-AUTH-019, TC-AUTH-020, TC-AUTH-021 | 3 |
| US-AUTH-008 | Cross-tenant user switching | TC-AUTH-022, TC-AUTH-023 | 2 |
| US-AUTH-009 | Session management and concurrent limits | TC-AUTH-024, TC-AUTH-025 | 2 |
| US-AUTH-010 | Account lockout after failed attempts | TC-AUTH-026, TC-AUTH-027, TC-AUTH-028 | 3 |
| Cross-cutting | Multi-tenant isolation | TC-AUTH-ISO-001, TC-AUTH-ISO-002, TC-AUTH-ISO-003, TC-AUTH-ISO-004 | 4 |

## Test Type Distribution

| Type | Test Cases | Count |
|------|------------|-------|
| Functional | TC-AUTH-001, TC-AUTH-004, TC-AUTH-005, TC-AUTH-006, TC-AUTH-007, TC-AUTH-008, TC-AUTH-010, TC-AUTH-011, TC-AUTH-013, TC-AUTH-014, TC-AUTH-016, TC-AUTH-019, TC-AUTH-022, TC-AUTH-024, TC-AUTH-025, TC-AUTH-028, TC-AUTH-039, TC-AUTH-040, TC-AUTH-042, TC-AUTH-043, TC-AUTH-046, TC-AUTH-047 | 22 |
| Security | TC-AUTH-002, TC-AUTH-003, TC-AUTH-009, TC-AUTH-012, TC-AUTH-015, TC-AUTH-017, TC-AUTH-018, TC-AUTH-020, TC-AUTH-021, TC-AUTH-023, TC-AUTH-026, TC-AUTH-027, TC-AUTH-041, TC-AUTH-045, TC-AUTH-050, TC-AUTH-ISO-001, TC-AUTH-ISO-002, TC-AUTH-ISO-003, TC-AUTH-ISO-004 | 19 |
| Performance | TC-AUTH-044, TC-AUTH-049 | 2 |
| Accessibility | TC-AUTH-048 | 1 |

## Test Category Coverage

| Category | Test Cases | Count |
|----------|------------|-------|
| Happy Path | TC-AUTH-001, TC-AUTH-005, TC-AUTH-006, TC-AUTH-007, TC-AUTH-008, TC-AUTH-010, TC-AUTH-011, TC-AUTH-013, TC-AUTH-014, TC-AUTH-016, TC-AUTH-019, TC-AUTH-022, TC-AUTH-024, TC-AUTH-025, TC-AUTH-028, TC-AUTH-039, TC-AUTH-046 | 17 |
| Negative Test | TC-AUTH-002, TC-AUTH-003, TC-AUTH-004, TC-AUTH-007, TC-AUTH-009, TC-AUTH-012, TC-AUTH-015, TC-AUTH-017, TC-AUTH-020, TC-AUTH-021, TC-AUTH-023, TC-AUTH-024, TC-AUTH-026, TC-AUTH-027, TC-AUTH-040, TC-AUTH-041, TC-AUTH-042, TC-AUTH-045, TC-AUTH-ISO-001, TC-AUTH-ISO-003, TC-AUTH-ISO-004 | 21 |
| Boundary Test | TC-AUTH-004, TC-AUTH-012, TC-AUTH-020, TC-AUTH-024, TC-AUTH-025, TC-AUTH-026, TC-AUTH-028, TC-AUTH-043, TC-AUTH-044 | 9 |
| Security Test | TC-AUTH-002, TC-AUTH-003, TC-AUTH-005, TC-AUTH-009, TC-AUTH-012, TC-AUTH-013, TC-AUTH-014, TC-AUTH-015, TC-AUTH-016, TC-AUTH-017, TC-AUTH-018, TC-AUTH-020, TC-AUTH-021, TC-AUTH-023, TC-AUTH-024, TC-AUTH-026, TC-AUTH-027, TC-AUTH-040, TC-AUTH-041, TC-AUTH-042, TC-AUTH-045, TC-AUTH-046, TC-AUTH-047, TC-AUTH-050, TC-AUTH-ISO-001, TC-AUTH-ISO-002, TC-AUTH-ISO-003, TC-AUTH-ISO-004 | 28 |
| Multi-Tenant Isolation | TC-AUTH-018, TC-AUTH-022, TC-AUTH-023, TC-AUTH-ISO-001, TC-AUTH-ISO-002, TC-AUTH-ISO-003, TC-AUTH-ISO-004 | 7 |
| Performance Test | TC-AUTH-044, TC-AUTH-047, TC-AUTH-049 | 3 |
| Accessibility Test | TC-AUTH-048 | 1 |

## Acceptance Criteria Coverage

| User Story | AC | Covered By Test Cases |
|------------|----|-----------------------|
| US-AUTH-001 | AC-1 | TC-AUTH-001 |
| US-AUTH-001 | AC-2 | TC-AUTH-002, TC-AUTH-003 |
| US-AUTH-001 | AC-3 | TC-AUTH-ISO-001 |
| US-AUTH-001 | AC-4 | TC-AUTH-021 |
| US-AUTH-001 | AC-5 | TC-AUTH-014 |
| US-AUTH-001 | AC-6 | TC-AUTH-020 |
| US-AUTH-002 | AC-1 | TC-AUTH-005 |
| US-AUTH-002 | AC-2 | TC-AUTH-006, TC-AUTH-007 |
| US-AUTH-002 | AC-3 | TC-AUTH-009 |
| US-AUTH-002 | AC-4 | TC-AUTH-007 |
| US-AUTH-002 | AC-5 | TC-AUTH-021 |
| US-AUTH-002 | AC-6 | TC-AUTH-009 |
| US-AUTH-002 | AC-7 | TC-AUTH-005 |
| US-AUTH-003 | AC-1 | TC-AUTH-008 |
| US-AUTH-003 | AC-2 | TC-AUTH-008, TC-AUTH-009 |
| US-AUTH-003 | AC-3 | TC-AUTH-009 |
| US-AUTH-003 | AC-4 | TC-AUTH-008 |
| US-AUTH-003 | AC-5 | TC-AUTH-008 |
| US-AUTH-004 | AC-1 | TC-AUTH-010 |
| US-AUTH-004 | AC-2 | TC-AUTH-010 |
| US-AUTH-004 | AC-3 | TC-AUTH-011 |
| US-AUTH-004 | AC-4 | TC-AUTH-012 |
| US-AUTH-004 | AC-5 | TC-AUTH-012 |
| US-AUTH-004 | AC-6 | TC-AUTH-011 |
| US-AUTH-005 | AC-1 | TC-AUTH-014 |
| US-AUTH-005 | AC-2 | TC-AUTH-013 |
| US-AUTH-005 | AC-3 | TC-AUTH-013 |
| US-AUTH-005 | AC-4 | TC-AUTH-014 |
| US-AUTH-005 | AC-5 | TC-AUTH-015 |
| US-AUTH-005 | AC-6 | TC-AUTH-013 |
| US-AUTH-005 | AC-7 | TC-AUTH-013 |
| US-AUTH-006 | AC-1 | TC-AUTH-016, TC-AUTH-039, TC-AUTH-048 |
| US-AUTH-006 | AC-2 | TC-AUTH-016, TC-AUTH-039, TC-AUTH-044, TC-AUTH-046, TC-AUTH-050 |
| US-AUTH-006 | AC-3 | TC-AUTH-016, TC-AUTH-039, TC-AUTH-042, TC-AUTH-043, TC-AUTH-046 |
| US-AUTH-006 | AC-4 | TC-AUTH-017, TC-AUTH-039, TC-AUTH-049, TC-AUTH-050 |
| US-AUTH-006 | AC-5 | TC-AUTH-017, TC-AUTH-045 |
| US-AUTH-006 | AC-6 | TC-AUTH-016, TC-AUTH-040, TC-AUTH-050 |
| US-AUTH-006 | AC-7 | TC-AUTH-018, TC-AUTH-ISO-004 |
| US-AUTH-007 | AC-1 | TC-AUTH-019 |
| US-AUTH-007 | AC-2 | TC-AUTH-020 |
| US-AUTH-007 | AC-3 | TC-AUTH-019 |
| US-AUTH-007 | AC-4 | TC-AUTH-019 |
| US-AUTH-007 | AC-5 | TC-AUTH-021 |
| US-AUTH-007 | AC-6 | TC-AUTH-019 |
| US-AUTH-008 | AC-1 | TC-AUTH-022 |
| US-AUTH-008 | AC-2 | TC-AUTH-022 |
| US-AUTH-008 | AC-3 | TC-AUTH-023 |
| US-AUTH-008 | AC-4 | TC-AUTH-023 |
| US-AUTH-008 | AC-5 | TC-AUTH-022 |
| US-AUTH-009 | AC-1 | TC-AUTH-024, TC-AUTH-025 |
| US-AUTH-009 | AC-2 | TC-AUTH-024 |
| US-AUTH-009 | AC-3 | TC-AUTH-024 |
| US-AUTH-009 | AC-4 | TC-AUTH-025 |
| US-AUTH-009 | AC-5 | TC-AUTH-025 |
| US-AUTH-009 | AC-6 | TC-AUTH-025 |
| US-AUTH-010 | AC-1 | TC-AUTH-026 |
| US-AUTH-010 | AC-2 | TC-AUTH-026 |
| US-AUTH-010 | AC-3 | TC-AUTH-027 |
| US-AUTH-010 | AC-4 | TC-AUTH-028 |
| US-AUTH-010 | AC-5 | TC-AUTH-028 |
| US-AUTH-010 | AC-6 | TC-AUTH-028 |

## API Endpoint Coverage

| Endpoint | Method | Covered By |
|----------|--------|------------|
| /api/v1/auth/login | POST | TC-AUTH-001, TC-AUTH-002, TC-AUTH-003, TC-AUTH-004, TC-AUTH-014, TC-AUTH-026, TC-AUTH-027, TC-AUTH-028 |
| /api/v1/auth/refresh | POST | TC-AUTH-006, TC-AUTH-007, TC-AUTH-009, TC-AUTH-046, TC-AUTH-047 |
| /api/v1/auth/logout | POST | TC-AUTH-008, TC-AUTH-009 |
| /api/v1/auth/forgot-password | POST | TC-AUTH-010 |
| /api/v1/auth/reset-password | POST | TC-AUTH-011, TC-AUTH-012 |
| /api/v1/auth/mfa/enroll | POST | TC-AUTH-013 |
| /api/v1/auth/mfa/verify | POST | TC-AUTH-014, TC-AUTH-015 |
| /api/v1/tenant/roles | GET | TC-AUTH-016, TC-AUTH-039, TC-AUTH-040, TC-AUTH-041, TC-AUTH-044, TC-AUTH-ISO-004 |
| /api/v1/tenant/roles | POST | TC-AUTH-016, TC-AUTH-039, TC-AUTH-041, TC-AUTH-044, TC-AUTH-050 |
| /api/v1/tenant/roles/{id} | GET | TC-AUTH-044, TC-AUTH-046, TC-AUTH-ISO-004 |
| /api/v1/tenant/roles/{id} | PUT | TC-AUTH-040, TC-AUTH-047, TC-AUTH-050 |
| /api/v1/tenant/roles/{id} | DELETE | TC-AUTH-040, TC-AUTH-046, TC-AUTH-050 |
| /api/v1/tenant/users | GET | TC-AUTH-016, TC-AUTH-017, TC-AUTH-042 |
| /api/v1/tenant/users/{id} | PATCH | TC-AUTH-039, TC-AUTH-042, TC-AUTH-043, TC-AUTH-046, TC-AUTH-047, TC-AUTH-050, TC-AUTH-ISO-004 |
| /api/v1/tenant/leave/requests | GET | TC-AUTH-039, TC-AUTH-049 |
| /api/v1/tenant/leave/requests/{id}/approve | POST | TC-AUTH-045 |
| /api/v1/tenant/payroll/runs | GET | TC-AUTH-017, TC-AUTH-039, TC-AUTH-049 |
| /api/v1/tenant/audit-log | GET | TC-AUTH-050 |
| /api/v1/auth/my-tenants | GET | TC-AUTH-022 |
| /api/v1/auth/switch-tenant | POST | TC-AUTH-022, TC-AUTH-023 |
| /api/v1/auth/me/sessions | GET | TC-AUTH-025 |
| /api/v1/tenant/users/{id}/sessions/revoke | POST | TC-AUTH-025 |

## FR/NFR/BR Coverage for US-AUTH-006

| Requirement | Covered By Test Cases |
|-------------|-----------------------|
| FR-1 (Module.Action.Scope pattern) | TC-AUTH-039, TC-AUTH-043, TC-AUTH-045 |
| FR-2 (Tenant-scoped roles, built-in protection) | TC-AUTH-040, TC-AUTH-041, TC-AUTH-ISO-004 |
| FR-3 (role_permission table) | TC-AUTH-039, TC-AUTH-043, TC-AUTH-047 |
| FR-4 (user_tenant_role many-to-many) | TC-AUTH-039, TC-AUTH-043, TC-AUTH-046, TC-AUTH-047 |
| FR-5 (Three-layer authorization) | TC-AUTH-039, TC-AUTH-045, TC-AUTH-049 |
| FR-6 (CRUD endpoints) | TC-AUTH-039, TC-AUTH-040, TC-AUTH-044, TC-AUTH-046, TC-AUTH-050 |
| FR-7 (Audit logging) | TC-AUTH-050 |
| FR-8 (Tenant Owner protection) | TC-AUTH-042 |
| FR-9 (System roles isolation) | TC-AUTH-041 |
| FR-10 (EF Core filters + RLS) | TC-AUTH-ISO-004 |
| NFR-1 (Permission eval <= 5ms) | TC-AUTH-049 |
| NFR-2 (Redis cache + invalidation) | TC-AUTH-047, TC-AUTH-ISO-004 |
| NFR-3 (50 roles / 200+ permissions) | TC-AUTH-044 |
| NFR-4 (Auth failure logging) | TC-AUTH-050 |
| BR-1 (Per-tenant-membership roles) | TC-AUTH-018, TC-AUTH-ISO-004 |
| BR-2 (Built-in immutable) | TC-AUTH-040 |
| BR-3 (Custom role permission subsets) | TC-AUTH-039 |
| BR-4 (Permission union) | TC-AUTH-043 |
| BR-5 (Effect on next token refresh) | TC-AUTH-046 |
| BR-6 (Tenant Owner minimum one) | TC-AUTH-042 |
| BR-7 (Delete role with users) | TC-AUTH-046 |
