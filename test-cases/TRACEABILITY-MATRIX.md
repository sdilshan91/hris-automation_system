---
title: Requirements Traceability Matrix
project: HRM SaaS Platform
created: 2026-05-11
status: draft
last_updated: 2026-05-11
---

# Requirements Traceability Matrix

This document links user stories to their corresponding test cases across all modules, ensuring complete requirements coverage per IEEE 829 and ISO/IEC/IEEE 29119 standards.

## Authentication & Authorization Module

### Forward Traceability (User Stories --> Test Cases)

| User Story ID | User Story Title | Priority | Test Cases | TC Count | Coverage |
|---------------|-----------------|----------|------------|----------|----------|
| US-AUTH-001 | Admin login with username and password | Must Have | TC-AUTH-001, TC-AUTH-002, TC-AUTH-003, TC-AUTH-004 | 4 | 6/6 AC covered |
| US-AUTH-002 | JWT token issuance and refresh token flow | Must Have | TC-AUTH-005, TC-AUTH-006, TC-AUTH-007 | 3 | 7/7 AC covered |
| US-AUTH-003 | User logout and token invalidation | Must Have | TC-AUTH-008, TC-AUTH-009 | 2 | 5/5 AC covered |
| US-AUTH-004 | Password reset flow | Must Have | TC-AUTH-010, TC-AUTH-011, TC-AUTH-012 | 3 | 6/6 AC covered |
| US-AUTH-005 | Multi-factor authentication (TOTP) | Should Have | TC-AUTH-013, TC-AUTH-014, TC-AUTH-015 | 3 | 7/7 AC covered |
| US-AUTH-006 | Role-based access control (RBAC) | Must Have | TC-AUTH-016, TC-AUTH-017, TC-AUTH-018 | 3 | 7/7 AC covered |
| US-AUTH-007 | Tenant resolution from subdomain | Must Have | TC-AUTH-019, TC-AUTH-020, TC-AUTH-021 | 3 | 6/6 AC covered |
| US-AUTH-008 | Cross-tenant user switching | Should Have | TC-AUTH-022, TC-AUTH-023 | 2 | 5/5 AC covered |
| US-AUTH-009 | Session management and concurrent limits | Should Have | TC-AUTH-024, TC-AUTH-025 | 2 | 6/6 AC covered |
| US-AUTH-010 | Account lockout after failed attempts | Must Have | TC-AUTH-026, TC-AUTH-027, TC-AUTH-028 | 3 | 6/6 AC covered |
| Cross-cutting | Multi-tenant isolation (mandatory) | Critical | TC-AUTH-ISO-001, TC-AUTH-ISO-002, TC-AUTH-ISO-003 | 3 | -- |
| **TOTAL** | | | **31 test cases** | **31** | **61/61 AC** |

### Backward Traceability (Test Cases --> User Stories)

| Test Case ID | Test Case Title | Type | Priority | User Story |
|-------------|----------------|------|----------|------------|
| TC-AUTH-001 | Successful login with valid credentials | Functional | Critical | US-AUTH-001 |
| TC-AUTH-002 | Login fails with wrong password | Security | Critical | US-AUTH-001 |
| TC-AUTH-003 | Login fails with non-existent username | Security | Critical | US-AUTH-001 |
| TC-AUTH-004 | Login form validation (empty fields) | Functional | High | US-AUTH-001 |
| TC-AUTH-005 | JWT issued on successful login | Functional | Critical | US-AUTH-002 |
| TC-AUTH-006 | Refresh token rotation works | Functional | Critical | US-AUTH-002 |
| TC-AUTH-007 | Expired access token triggers refresh | Functional | Critical | US-AUTH-002 |
| TC-AUTH-008 | Logout invalidates tokens | Functional | Critical | US-AUTH-003 |
| TC-AUTH-009 | Refresh token cannot be reused after logout | Security | Critical | US-AUTH-003 |
| TC-AUTH-010 | Forgot password sends reset email | Functional | Critical | US-AUTH-004 |
| TC-AUTH-011 | Reset password with valid token works | Functional | Critical | US-AUTH-004 |
| TC-AUTH-012 | Reset with expired/invalid token fails | Security | Critical | US-AUTH-004 |
| TC-AUTH-013 | Enable TOTP for user | Functional | High | US-AUTH-005 |
| TC-AUTH-014 | Login with valid TOTP code | Functional | Critical | US-AUTH-005 |
| TC-AUTH-015 | Login with invalid TOTP code fails | Security | Critical | US-AUTH-005 |
| TC-AUTH-016 | User with Admin role can access admin endpoints | Functional | Critical | US-AUTH-006 |
| TC-AUTH-017 | User without Admin role is denied admin access | Security | Critical | US-AUTH-006 |
| TC-AUTH-018 | Roles are tenant-scoped | Security | Critical | US-AUTH-006 |
| TC-AUTH-019 | Valid subdomain resolves to correct tenant | Functional | Critical | US-AUTH-007 |
| TC-AUTH-020 | Unknown subdomain returns 404 | Security | Critical | US-AUTH-007 |
| TC-AUTH-021 | Suspended tenant returns 403 | Security | Critical | US-AUTH-007 |
| TC-AUTH-022 | User switches tenant without re-auth | Functional | High | US-AUTH-008 |
| TC-AUTH-023 | User cannot switch to tenant they don't belong to | Security | Critical | US-AUTH-008 |
| TC-AUTH-024 | Concurrent session limit enforced | Functional | High | US-AUTH-009 |
| TC-AUTH-025 | Oldest session terminated when limit exceeded | Functional | High | US-AUTH-009 |
| TC-AUTH-026 | Account locked after N failed attempts | Security | Critical | US-AUTH-010 |
| TC-AUTH-027 | Locked account cannot login | Security | Critical | US-AUTH-010 |
| TC-AUTH-028 | Account unlocks after cooldown period | Functional | Critical | US-AUTH-010 |
| TC-AUTH-ISO-001 | Tenant A user cannot authenticate as Tenant B | Security | Critical | US-AUTH-001, US-AUTH-007 |
| TC-AUTH-ISO-002 | JWT claims include correct tenant_id | Security | Critical | US-AUTH-002, US-AUTH-006 |
| TC-AUTH-ISO-003 | API rejects requests with mismatched tenant context | Security | Critical | US-AUTH-002, US-AUTH-007 |

### Coverage Summary

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 61/61 (100%) | >= 100% | PASS |
| Multi-Tenant Isolation Tests | 6 (3 dedicated + 3 embedded) | >= 3 | PASS |
| Security Test Cases | 15/31 (48%) | >= 30% | PASS |
| Critical Module Coverage | 100% | >= 85% | PASS |
| API Endpoint Coverage | 13/13 (100%) | >= 90% | PASS |

---

*Note: This traceability matrix will be extended as test cases for additional modules (Employee Management, Leave Management, Attendance, Payroll, etc.) are authored.*
