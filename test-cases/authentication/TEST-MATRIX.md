---
module: Authentication & Authorization
total_user_stories: 10
total_test_cases: 41
created: 2026-05-11
updated: 2026-06-03
status: draft
---

# Authentication & Authorization -- Test Matrix

## Summary

| Metric | Value |
|--------|-------|
| Total User Stories | 10 |
| Total Test Cases | 34 (functional/security/perf/a11y) + 3 (isolation) + 4 (new security) = 41 |
| Critical Priority | 25 |
| High Priority | 10 |
| Medium Priority | 2 |
| Low Priority | 0 |
| Status | All Draft |

## User Story to Test Case Matrix

| User Story | Title | Test Cases | Count |
|------------|-------|------------|-------|
| US-AUTH-001 | Admin login with username and password | TC-AUTH-001, TC-AUTH-002, TC-AUTH-003, TC-AUTH-004 | 4 |
| US-AUTH-002 | JWT token issuance and refresh token flow | TC-AUTH-005, TC-AUTH-006, TC-AUTH-007 | 3 |
| US-AUTH-003 | User logout and token invalidation | TC-AUTH-008, TC-AUTH-009 | 2 |
| US-AUTH-004 | Password reset flow | TC-AUTH-010, TC-AUTH-011, TC-AUTH-012 | 3 |
| US-AUTH-005 | Multi-factor authentication (TOTP) | TC-AUTH-013, TC-AUTH-014, TC-AUTH-015, TC-AUTH-029, TC-AUTH-030, TC-AUTH-031, TC-AUTH-032, TC-AUTH-033, TC-AUTH-034, TC-AUTH-035, TC-AUTH-036, TC-AUTH-037, TC-AUTH-038 | 13 |
| US-AUTH-006 | Role-based access control (RBAC) | TC-AUTH-016, TC-AUTH-017, TC-AUTH-018 | 3 |
| US-AUTH-007 | Tenant resolution from subdomain | TC-AUTH-019, TC-AUTH-020, TC-AUTH-021 | 3 |
| US-AUTH-008 | Cross-tenant user switching | TC-AUTH-022, TC-AUTH-023 | 2 |
| US-AUTH-009 | Session management and concurrent limits | TC-AUTH-024, TC-AUTH-025 | 2 |
| US-AUTH-010 | Account lockout after failed attempts | TC-AUTH-026, TC-AUTH-027, TC-AUTH-028 | 3 |
| Cross-cutting | Multi-tenant isolation | TC-AUTH-ISO-001, TC-AUTH-ISO-002, TC-AUTH-ISO-003 | 3 |

## Test Type Distribution

| Type | Test Cases | Count |
|------|------------|-------|
| Functional | TC-AUTH-001, TC-AUTH-004, TC-AUTH-005, TC-AUTH-006, TC-AUTH-007, TC-AUTH-008, TC-AUTH-010, TC-AUTH-011, TC-AUTH-013, TC-AUTH-014, TC-AUTH-016, TC-AUTH-019, TC-AUTH-022, TC-AUTH-024, TC-AUTH-025, TC-AUTH-028, TC-AUTH-029, TC-AUTH-030, TC-AUTH-032, TC-AUTH-033 | 20 |
| Security | TC-AUTH-002, TC-AUTH-003, TC-AUTH-009, TC-AUTH-012, TC-AUTH-015, TC-AUTH-017, TC-AUTH-018, TC-AUTH-020, TC-AUTH-021, TC-AUTH-023, TC-AUTH-026, TC-AUTH-027, TC-AUTH-031, TC-AUTH-034, TC-AUTH-035, TC-AUTH-037, TC-AUTH-ISO-001, TC-AUTH-ISO-002, TC-AUTH-ISO-003 | 19 |
| Performance | TC-AUTH-036 | 1 |
| Accessibility | TC-AUTH-038 | 1 |

## Test Category Coverage

| Category | Test Cases | Count |
|----------|------------|-------|
| Happy Path | TC-AUTH-001, TC-AUTH-005, TC-AUTH-006, TC-AUTH-007, TC-AUTH-008, TC-AUTH-010, TC-AUTH-011, TC-AUTH-013, TC-AUTH-014, TC-AUTH-016, TC-AUTH-019, TC-AUTH-022, TC-AUTH-024, TC-AUTH-025, TC-AUTH-028, TC-AUTH-029, TC-AUTH-030, TC-AUTH-032, TC-AUTH-033 | 19 |
| Negative Test | TC-AUTH-002, TC-AUTH-003, TC-AUTH-004, TC-AUTH-007, TC-AUTH-009, TC-AUTH-012, TC-AUTH-015, TC-AUTH-017, TC-AUTH-020, TC-AUTH-021, TC-AUTH-023, TC-AUTH-024, TC-AUTH-026, TC-AUTH-027, TC-AUTH-031, TC-AUTH-032, TC-AUTH-034, TC-AUTH-035, TC-AUTH-037, TC-AUTH-ISO-001, TC-AUTH-ISO-003 | 21 |
| Boundary Test | TC-AUTH-004, TC-AUTH-012, TC-AUTH-020, TC-AUTH-024, TC-AUTH-025, TC-AUTH-026, TC-AUTH-028 | 7 |
| Security Test | TC-AUTH-002, TC-AUTH-003, TC-AUTH-005, TC-AUTH-009, TC-AUTH-012, TC-AUTH-013, TC-AUTH-014, TC-AUTH-015, TC-AUTH-016, TC-AUTH-017, TC-AUTH-018, TC-AUTH-020, TC-AUTH-021, TC-AUTH-023, TC-AUTH-024, TC-AUTH-026, TC-AUTH-027, TC-AUTH-029, TC-AUTH-030, TC-AUTH-031, TC-AUTH-032, TC-AUTH-034, TC-AUTH-035, TC-AUTH-037, TC-AUTH-ISO-001, TC-AUTH-ISO-002, TC-AUTH-ISO-003 | 27 |
| Multi-Tenant Isolation | TC-AUTH-018, TC-AUTH-022, TC-AUTH-023, TC-AUTH-037, TC-AUTH-ISO-001, TC-AUTH-ISO-002, TC-AUTH-ISO-003 | 7 |
| Performance Test | TC-AUTH-036 | 1 |
| Accessibility Test | TC-AUTH-038 | 1 |

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
| US-AUTH-005 | AC-1 | TC-AUTH-029 |
| US-AUTH-005 | AC-2 | TC-AUTH-013 |
| US-AUTH-005 | AC-3 | TC-AUTH-013 |
| US-AUTH-005 | AC-4 | TC-AUTH-014 |
| US-AUTH-005 | AC-5 | TC-AUTH-015 |
| US-AUTH-005 | AC-6 | TC-AUTH-033 |
| US-AUTH-005 | AC-7 | TC-AUTH-030 |
| US-AUTH-006 | AC-1 | TC-AUTH-016 |
| US-AUTH-006 | AC-2 | TC-AUTH-016 |
| US-AUTH-006 | AC-3 | TC-AUTH-016 |
| US-AUTH-006 | AC-4 | TC-AUTH-017 |
| US-AUTH-006 | AC-5 | TC-AUTH-017 |
| US-AUTH-006 | AC-6 | TC-AUTH-016 |
| US-AUTH-006 | AC-7 | TC-AUTH-018 |
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
| /api/v1/auth/login | POST | TC-AUTH-001, TC-AUTH-002, TC-AUTH-003, TC-AUTH-004, TC-AUTH-014, TC-AUTH-026, TC-AUTH-027, TC-AUTH-028, TC-AUTH-029, TC-AUTH-030, TC-AUTH-037 |
| /api/v1/auth/refresh | POST | TC-AUTH-006, TC-AUTH-007, TC-AUTH-009 |
| /api/v1/auth/logout | POST | TC-AUTH-008, TC-AUTH-009 |
| /api/v1/auth/forgot-password | POST | TC-AUTH-010 |
| /api/v1/auth/reset-password | POST | TC-AUTH-011, TC-AUTH-012 |
| /api/v1/auth/mfa/enroll | POST | TC-AUTH-013, TC-AUTH-029, TC-AUTH-033, TC-AUTH-035 |
| /api/v1/auth/mfa/verify | POST | TC-AUTH-013, TC-AUTH-014, TC-AUTH-015, TC-AUTH-029, TC-AUTH-033 |
| /api/v1/auth/mfa/challenge | POST | TC-AUTH-030, TC-AUTH-031, TC-AUTH-036, TC-AUTH-037 |
| /api/v1/auth/mfa | DELETE | TC-AUTH-033, TC-AUTH-034, TC-AUTH-037 |
| /api/v1/tenant/auth-settings | GET | TC-AUTH-032 |
| /api/v1/tenant/auth-settings | PUT | TC-AUTH-032, TC-AUTH-034, TC-AUTH-037 |
| /api/v1/tenant/roles | GET/POST | TC-AUTH-016 |
| /api/v1/tenant/users | GET | TC-AUTH-016, TC-AUTH-017 |
| /api/v1/auth/my-tenants | GET | TC-AUTH-022 |
| /api/v1/auth/switch-tenant | POST | TC-AUTH-022, TC-AUTH-023, TC-AUTH-037 |
| /api/v1/auth/me/sessions | GET | TC-AUTH-025 |
| /api/v1/tenant/users/{id}/sessions/revoke | POST | TC-AUTH-025 |
