---
title: Requirements Traceability Matrix
project: HRM SaaS Platform
created: 2026-05-11
status: draft
last_updated: 2026-06-09
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
| US-AUTH-005 | Multi-factor authentication (TOTP) | Should Have | TC-AUTH-013, TC-AUTH-014, TC-AUTH-015, TC-AUTH-029, TC-AUTH-030, TC-AUTH-031, TC-AUTH-032, TC-AUTH-033, TC-AUTH-034, TC-AUTH-035, TC-AUTH-036, TC-AUTH-037, TC-AUTH-038 | 13 | 7/7 AC covered |
| US-AUTH-006 | Role-based access control (RBAC) | Must Have | TC-AUTH-016, TC-AUTH-017, TC-AUTH-018, TC-AUTH-039, TC-AUTH-040, TC-AUTH-041, TC-AUTH-042, TC-AUTH-043, TC-AUTH-044, TC-AUTH-045, TC-AUTH-046, TC-AUTH-047, TC-AUTH-048, TC-AUTH-049, TC-AUTH-050 | 15 | 7/7 AC covered (deep) |
| US-AUTH-007 | Tenant resolution from subdomain | Must Have | TC-AUTH-019, TC-AUTH-020, TC-AUTH-021, TC-AUTH-051, TC-AUTH-052, TC-AUTH-053, TC-AUTH-054, TC-AUTH-055, TC-AUTH-056, TC-AUTH-057, TC-AUTH-058 | 11 | 6/6 AC covered (deep) |
| US-AUTH-008 | Cross-tenant user switching | Should Have | TC-AUTH-022, TC-AUTH-023, TC-AUTH-059, TC-AUTH-060, TC-AUTH-061, TC-AUTH-062, TC-AUTH-063, TC-AUTH-064 | 8 | 5/5 AC covered (deep) |
| US-AUTH-009 | Session management and concurrent limits | Should Have | TC-AUTH-024, TC-AUTH-025 | 2 | 6/6 AC covered |
| US-AUTH-010 | Account lockout after failed attempts | Must Have | TC-AUTH-026, TC-AUTH-027, TC-AUTH-028 | 3 | 6/6 AC covered |
| Cross-cutting | Multi-tenant isolation (mandatory) | Critical | TC-AUTH-ISO-001, TC-AUTH-ISO-002, TC-AUTH-ISO-003, TC-AUTH-ISO-004 | 4 | -- |
| **TOTAL** | | | **68 test cases** | **68** | **61/61 AC** |

### Backward Traceability (Test Cases --> User Stories)

| Test Case ID | Test Case Title | Type | Priority | User Story | Requirements Covered |
|-------------|----------------|------|----------|------------|---------------------|
| TC-AUTH-001 | Successful login with valid credentials | Functional | Critical | US-AUTH-001 | AC-1 |
| TC-AUTH-002 | Login fails with wrong password | Security | Critical | US-AUTH-001 | AC-2 |
| TC-AUTH-003 | Login fails with non-existent username | Security | Critical | US-AUTH-001 | AC-2 |
| TC-AUTH-004 | Login form validation (empty fields) | Functional | High | US-AUTH-001 | AC-1 |
| TC-AUTH-005 | JWT issued on successful login | Functional | Critical | US-AUTH-002 | AC-1, AC-7 |
| TC-AUTH-006 | Refresh token rotation works | Functional | Critical | US-AUTH-002 | AC-2 |
| TC-AUTH-007 | Expired access token triggers refresh | Functional | Critical | US-AUTH-002 | AC-2, AC-4 |
| TC-AUTH-008 | Logout invalidates tokens | Functional | Critical | US-AUTH-003 | AC-1, AC-2, AC-4, AC-5 |
| TC-AUTH-009 | Refresh token cannot be reused after logout | Security | Critical | US-AUTH-003 | AC-2, AC-3 |
| TC-AUTH-010 | Forgot password sends reset email | Functional | Critical | US-AUTH-004 | AC-1, AC-2 |
| TC-AUTH-011 | Reset password with valid token works | Functional | Critical | US-AUTH-004 | AC-3, AC-6 |
| TC-AUTH-012 | Reset with expired/invalid token fails | Security | Critical | US-AUTH-004 | AC-4, AC-5 |
| TC-AUTH-013 | Enable TOTP for user | Functional | High | US-AUTH-005 | AC-2, AC-3, FR-1, FR-2, FR-3, FR-4, FR-5, FR-10 |
| TC-AUTH-014 | Login with valid TOTP code | Functional | Critical | US-AUTH-005 | AC-4, FR-2, FR-3, FR-10 |
| TC-AUTH-015 | Login with invalid TOTP code fails | Security | Critical | US-AUTH-005 | AC-5, FR-2, FR-3, NFR-4 |
| TC-AUTH-016 | User with Admin role can access admin endpoints | Functional | Critical | US-AUTH-006 | AC-1, AC-2, AC-3, AC-6 |
| TC-AUTH-017 | User without Admin role is denied admin access | Security | Critical | US-AUTH-006 | AC-4, AC-5 |
| TC-AUTH-018 | Roles are tenant-scoped | Security | Critical | US-AUTH-006 | AC-7 |
| TC-AUTH-019 | Valid subdomain resolves to correct tenant | Functional | Critical | US-AUTH-007 | AC-1, AC-3, AC-4, AC-6 |
| TC-AUTH-020 | Unknown subdomain returns 404 | Security | Critical | US-AUTH-007 | AC-2 |
| TC-AUTH-021 | Suspended tenant returns 403 | Security | Critical | US-AUTH-007 | AC-5 |
| TC-AUTH-022 | User switches tenant without re-auth | Functional | High | US-AUTH-008 | AC-1, AC-2, AC-5 |
| TC-AUTH-023 | User cannot switch to tenant they don't belong to | Security | Critical | US-AUTH-008 | AC-3, AC-4 |
| TC-AUTH-024 | Concurrent session limit enforced | Functional | High | US-AUTH-009 | AC-1, AC-2, AC-3 |
| TC-AUTH-025 | Oldest session terminated when limit exceeded | Functional | High | US-AUTH-009 | AC-4, AC-5, AC-6 |
| TC-AUTH-026 | Account locked after N failed attempts | Security | Critical | US-AUTH-010 | AC-1, AC-2 |
| TC-AUTH-027 | Locked account cannot login | Security | Critical | US-AUTH-010 | AC-3 |
| TC-AUTH-028 | Account unlocks after cooldown period | Functional | Critical | US-AUTH-010 | AC-4, AC-5, AC-6 |
| TC-AUTH-029 | Forced MFA enrollment when tenant policy requires it for user's role | Functional | Critical | US-AUTH-005 | AC-1, FR-6, FR-7, BR-1, BR-5 |
| TC-AUTH-030 | Login with valid recovery code | Functional | High | US-AUTH-005 | AC-7, FR-2, FR-5, BR-4 |
| TC-AUTH-031 | Recovery code reuse rejection | Security | Critical | US-AUTH-005 | AC-7 (neg), BR-4, NFR-4 |
| TC-AUTH-032 | Tenant admin updates MFA policy | Functional | High | US-AUTH-005 | AC-1, AC-6, FR-6, FR-8, BR-1, BR-5 |
| TC-AUTH-033 | Optional policy -- user freely enables and disables MFA | Functional | High | US-AUTH-005 | AC-6, FR-1, FR-2, FR-8, FR-9, BR-3 |
| TC-AUTH-034 | Disable MFA blocked when tenant policy requires it for user's role | Security | High | US-AUTH-005 | AC-1, AC-6 (neg), FR-9, BR-3 |
| TC-AUTH-035 | Recovery codes cannot be retrieved after enrollment | Security | High | US-AUTH-005 | AC-2, AC-3, FR-1, FR-5, NFR-3 |
| TC-AUTH-036 | MFA verification performance and rate limiting | Performance | Medium | US-AUTH-005 | NFR-1, NFR-4 |
| TC-AUTH-037 | Cross-tenant MFA enforcement | Security | High | US-AUTH-005 | AC-1, AC-6, FR-6, FR-7, FR-9, BR-2, BR-3 |
| TC-AUTH-038 | Accessibility of MFA enrollment and challenge UI | Accessibility | Medium | US-AUTH-005 | AC-2, AC-3, AC-4, AC-7, UI/UX section 8 |
| TC-AUTH-039 | Create custom role, assign to user, verify permitted and blocked access | Functional | Critical | US-AUTH-006 | AC-1, AC-2, AC-3, AC-4, FR-1, FR-3, FR-4, FR-5, FR-6, BR-3 |
| TC-AUTH-040 | Edit or delete a built-in role is rejected | Functional | Critical | US-AUTH-006 | AC-6, FR-2, FR-6, BR-2 |
| TC-AUTH-041 | System role creation in a regular tenant is rejected | Security | High | US-AUTH-006 | FR-2, FR-9 |
| TC-AUTH-042 | Remove Tenant Owner role from sole owner is rejected | Functional | Critical | US-AUTH-006 | AC-3, FR-8, BR-6 |
| TC-AUTH-043 | Permission union when user has two overlapping roles | Functional | High | US-AUTH-006 | AC-3, FR-1, FR-3, FR-4, BR-4 |
| TC-AUTH-044 | System supports 50 custom roles per tenant and 200+ permissions | Performance | High | US-AUTH-006 | AC-2, FR-6, NFR-3 |
| TC-AUTH-045 | Resource-level authorization blocks manager from approving non-report leave | Security | Critical | US-AUTH-006 | AC-4, AC-5, FR-1, FR-5 |
| TC-AUTH-046 | Deleting a custom role removes it from assigned users and updates JWT on refresh | Functional | Critical | US-AUTH-006 | AC-2, AC-3, FR-4, FR-6, BR-5, BR-7 |
| TC-AUTH-047 | Redis cache invalidation on role or permission change | Functional | High | US-AUTH-006 | FR-3, FR-4, NFR-2 |
| TC-AUTH-048 | Roles management UI accessibility (WCAG 2.1 AA) | Accessibility | Medium | US-AUTH-006 | AC-1 |
| TC-AUTH-049 | Permission evaluation adds no more than 5ms overhead per request | Performance | High | US-AUTH-006 | AC-4, FR-5, NFR-1 |
| TC-AUTH-050 | Role and permission changes are audited in tenant audit log | Security | High | US-AUTH-006 | AC-2, AC-4, AC-6, FR-7, NFR-4 |
| TC-AUTH-051 | Reserved subdomains bypass tenant resolution | Functional | High | US-AUTH-007 | AC-3, FR-1, FR-3, BR-2, BR-4 |
| TC-AUTH-052 | Admin subdomain establishes system context with authorization enforcement | Security | Critical | US-AUTH-007 | AC-4, FR-1, FR-3, FR-4, FR-6, BR-3, BR-4 |
| TC-AUTH-053 | Subdomain validation rejects invalid and boundary values | Security | High | US-AUTH-007 | AC-2, FR-2, FR-7, NFR-4, NFR-5, BR-1, BR-2, BR-5 |
| TC-AUTH-054 | Resolved tenant context prevents cross-tenant data exposure | Security | Critical | US-AUTH-007 | AC-1, FR-1, FR-6, FR-10, BR-4 |
| TC-AUTH-055 | Tenant resolution cache hit completes within 5 ms | Performance | High | US-AUTH-007 | AC-1, FR-5, FR-6, FR-9, NFR-1 |
| TC-AUTH-056 | Cache miss falls back to PostgreSQL and repopulates Redis within 50 ms | Performance | High | US-AUTH-007 | AC-6, FR-5, FR-6, FR-9, NFR-2 |
| TC-AUTH-057 | Redis outage falls back to PostgreSQL without failing tenant requests | Performance | High | US-AUTH-007 | AC-1, AC-6, FR-5, FR-6, NFR-3 |
| TC-AUTH-058 | Static 404 and suspended workspace pages meet accessibility and information disclosure rules | Accessibility | Medium | US-AUTH-007 | AC-2, AC-5, FR-7, FR-8, NFR-5 |
| TC-AUTH-059 | My-tenants list and switch to tenant B | Functional | High | US-AUTH-008 | AC-1, AC-2, FR-1, FR-2, FR-3, FR-5, FR-6, FR-8, FR-9, BR-1, BR-5 |
| TC-AUTH-060 | Unauthorized, suspended, and terminated tenant switch attempts are rejected | Security | Critical | US-AUTH-008 | AC-3, AC-4, FR-5, FR-6, BR-5 |
| TC-AUTH-061 | Target JWT contains only target tenant roles and permissions | Security | Critical | US-AUTH-008 | AC-5, FR-3, FR-5, BR-2, NFR-3 |
| TC-AUTH-062 | Tenant switch blocks impersonation and prevents cross-tenant data exposure | Security | Critical | US-AUTH-008 | AC-2, AC-3, AC-5, FR-3, FR-5, FR-7, BR-4, NFR-3, NFR-4 |
| TC-AUTH-063 | Source session remains valid and MFA-required target triggers enrollment | Functional | High | US-AUTH-008 | AC-2, AC-5, FR-3, FR-4, FR-6, FR-8, BR-1, BR-3 |
| TC-AUTH-064 | My-tenants cache performance and invalidation | Performance | Medium | US-AUTH-008 | AC-1, FR-1, FR-9, NFR-1, NFR-2, NFR-3, NFR-4, BR-5 |
| TC-AUTH-ISO-001 | Tenant A user cannot authenticate as Tenant B | Security | Critical | US-AUTH-001, US-AUTH-007 | -- |
| TC-AUTH-ISO-002 | JWT claims include correct tenant_id | Security | Critical | US-AUTH-002, US-AUTH-006 | -- |
| TC-AUTH-ISO-003 | API rejects requests with mismatched tenant context | Security | Critical | US-AUTH-002, US-AUTH-007 | -- |
| TC-AUTH-ISO-004 | RBAC cross-tenant isolation -- roles, permissions, and cache keys are tenant-scoped | Security | Critical | US-AUTH-006 | FR-2, FR-10, NFR-2, BR-1 |

### US-AUTH-008 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: my-tenants returns tenant memberships with tenant fields and roles | AC | TC-AUTH-022, TC-AUTH-059, TC-AUTH-064 | Direct |
| AC-2: Switch to tenant B issues target tokens and redirects to target subdomain | AC | TC-AUTH-022, TC-AUTH-059, TC-AUTH-062, TC-AUTH-063 | Direct |
| AC-3: Unauthorized tenant switch returns 403 | AC | TC-AUTH-023, TC-AUTH-060, TC-AUTH-062 | Direct |
| AC-4: Suspended tenant switch returns 403 and UI flags unavailable tenant | AC | TC-AUTH-023, TC-AUTH-060 | Direct |
| AC-5: Target JWT contains only target tenant roles and source refresh remains valid | AC | TC-AUTH-022, TC-AUTH-061, TC-AUTH-062, TC-AUTH-063 | Direct |
| FR-1: GET /api/v1/auth/my-tenants returns all memberships | FR | TC-AUTH-022, TC-AUTH-059, TC-AUTH-064 | Direct |
| FR-2: POST /api/v1/auth/switch-tenant accepts tenantId UUID | FR | TC-AUTH-022, TC-AUTH-059, TC-AUTH-060 | Direct |
| FR-3: Switch issues new JWT and refresh token scoped to target tenant | FR | TC-AUTH-022, TC-AUTH-059, TC-AUTH-061, TC-AUTH-063 | Direct |
| FR-4: Previous tenant refresh token remains valid | FR | TC-AUTH-022, TC-AUTH-060, TC-AUTH-063 | Direct |
| FR-5: Active target membership is verified before token issuance | FR | TC-AUTH-023, TC-AUTH-060, TC-AUTH-062 | Direct |
| FR-6: Target tenant lifecycle allows login | FR | TC-AUTH-022, TC-AUTH-059, TC-AUTH-060, TC-AUTH-063 | Direct |
| FR-7: Tenant switch events are audited in source and target logs | FR | TC-AUTH-022, TC-AUTH-060, TC-AUTH-062, TC-AUTH-063 | Direct |
| FR-8: Frontend redirects browser to target tenant subdomain URL | FR | TC-AUTH-022, TC-AUTH-059, TC-AUTH-063 | Direct |
| FR-9: GET /api/v1/auth/me returns profile, current tenant, and memberships | FR | TC-AUTH-059, TC-AUTH-064 | Direct |
| NFR-1: Tenant switch response time <= 400 ms P95 | NFR | TC-AUTH-064 | Direct |
| NFR-2: my-tenants Redis cache is per user and invalidated on membership changes | NFR | TC-AUTH-064 | Direct |
| NFR-3: Switching exposes no source tenant data | NFR | TC-AUTH-061, TC-AUTH-062, TC-AUTH-064 | Direct |
| NFR-4: Switch works behind a load balancer | NFR | TC-AUTH-064 | Direct |
| BR-1: Single login session can switch tenants without re-authentication | BR | TC-AUTH-022, TC-AUTH-059, TC-AUTH-063 | Direct |
| BR-2: Roles are per membership | BR | TC-AUTH-022, TC-AUTH-061 | Direct |
| BR-3: MFA-required target triggers mandatory enrollment | BR | TC-AUTH-063 | Direct |
| BR-4: Impersonation sessions cannot use tenant switching | BR | TC-AUTH-062 | Direct |
| BR-5: my-tenants includes all memberships and flags inaccessible tenants | BR | TC-AUTH-059, TC-AUTH-060, TC-AUTH-064 | Direct |

### US-AUTH-007 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Active tenant subdomain resolves and populates context | AC | TC-AUTH-019, TC-AUTH-054, TC-AUTH-055, TC-AUTH-057 | Direct |
| AC-2: Unknown tenant returns static 404 with no exposed app/API | AC | TC-AUTH-020, TC-AUTH-053, TC-AUTH-058 | Direct |
| AC-3: Reserved subdomains route without tenant resolution | AC | TC-AUTH-051 | Direct |
| AC-4: Admin subdomain sets system context | AC | TC-AUTH-052 | Direct |
| AC-5: Suspended tenant shows suspension notice and blocks login | AC | TC-AUTH-021, TC-AUTH-058 | Direct |
| AC-6: Cache miss falls back to PostgreSQL and repopulates cache | AC | TC-AUTH-019, TC-AUTH-056, TC-AUTH-057 | Direct |
| FR-1: Middleware runs before authentication and authorization | FR | TC-AUTH-019, TC-AUTH-051, TC-AUTH-052 | Direct |
| FR-2: Host header subdomain extraction | FR | TC-AUTH-019, TC-AUTH-053 | Direct |
| FR-3: Reserved subdomain list | FR | TC-AUTH-051, TC-AUTH-052 | Direct |
| FR-4: `admin.yourhrm.com` sets system context | FR | TC-AUTH-052 | Direct |
| FR-5: Redis lookup then PostgreSQL fallback | FR | TC-AUTH-019, TC-AUTH-055, TC-AUTH-056, TC-AUTH-057 | Direct |
| FR-6: `ITenantContext` fields are populated | FR | TC-AUTH-019, TC-AUTH-052, TC-AUTH-054, TC-AUTH-055, TC-AUTH-056, TC-AUTH-057 | Direct |
| FR-7: Not-found tenants short-circuit with static 404 | FR | TC-AUTH-020, TC-AUTH-053, TC-AUTH-058 | Direct |
| FR-8: Non-accessible tenant state is preserved for downstream handling | FR | TC-AUTH-021, TC-AUTH-058 | Direct |
| FR-9: Redis TTL and cache population | FR | TC-AUTH-019, TC-AUTH-055, TC-AUTH-056 | Direct |
| FR-10: Tenant ID included in logs after resolution | FR | TC-AUTH-019, TC-AUTH-054 | Direct |
| NFR-1: Cache hit overhead <= 5 ms | NFR | TC-AUTH-055 | Direct |
| NFR-2: Cache miss P95 <= 50 ms | NFR | TC-AUTH-056 | Direct |
| NFR-3: Redis outage graceful fallback | NFR | TC-AUTH-057 | Direct |
| NFR-4: Valid subdomain format only | NFR | TC-AUTH-053 | Direct |
| NFR-5: Static 404 does not leak platform information | NFR | TC-AUTH-020, TC-AUTH-053, TC-AUTH-058 | Direct |
| BR-1: Unique immutable subdomain slug | BR | TC-AUTH-053 | Direct |
| BR-2: Reserved subdomains cannot be claimed | BR | TC-AUTH-051, TC-AUTH-053 | Direct |
| BR-3: System tenant special case | BR | TC-AUTH-052 | Direct |
| BR-4: Tenant resolution required except allowed routes | BR | TC-AUTH-051, TC-AUTH-052, TC-AUTH-054 | Direct |
| BR-5: Custom domains deferred to Phase 2 | BR | TC-AUTH-053 | Direct |

### US-AUTH-005 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Forced enrollment when policy=required | AC | TC-AUTH-029 | Direct |
| AC-2: TOTP enrollment returns secret, QR, recovery codes | AC | TC-AUTH-013, TC-AUTH-035 | Direct |
| AC-3: Verify TOTP code completes enrollment | AC | TC-AUTH-013, TC-AUTH-035 | Direct |
| AC-4: MFA-enabled user login triggers challenge | AC | TC-AUTH-014 | Direct |
| AC-5: Invalid TOTP code rejected, lockout after 5 | AC | TC-AUTH-015 | Direct |
| AC-6: Optional policy allows free enable/disable | AC | TC-AUTH-033, TC-AUTH-034 (neg) | Direct |
| AC-7: Recovery code login, marked used | AC | TC-AUTH-030, TC-AUTH-031 (neg) | Direct |
| FR-1: POST /api/v1/auth/mfa/enroll returns secret/QR/codes | FR | TC-AUTH-013, TC-AUTH-029, TC-AUTH-033, TC-AUTH-035 | Direct |
| FR-2: POST /api/v1/auth/mfa/verify accepts code | FR | TC-AUTH-013, TC-AUTH-014, TC-AUTH-015 | Direct |
| FR-3: TOTP uses Otp.NET, SHA1, 6-digit, 30s step | FR | TC-AUTH-013, TC-AUTH-014 | Direct |
| FR-4: TOTP secret encrypted at column level | FR | TC-AUTH-013 | Direct |
| FR-5: 10 single-use recovery codes, stored as hashes | FR | TC-AUTH-013, TC-AUTH-030, TC-AUTH-031, TC-AUTH-035 | Direct |
| FR-6: PUT /api/v1/tenant/auth-settings for MFA policy | FR | TC-AUTH-032, TC-AUTH-037 | Direct |
| FR-7: Required MFA redirects to enrollment | FR | TC-AUTH-029, TC-AUTH-037 | Direct |
| FR-8: Enrollment/disable events in audit log | FR | TC-AUTH-013, TC-AUTH-032, TC-AUTH-033 | Direct |
| FR-9: Disable MFA from profile if policy allows | FR | TC-AUTH-033, TC-AUTH-034 | Direct |
| FR-10: TOTP +/- 1 time-step tolerance | FR | TC-AUTH-013, TC-AUTH-014 | Direct |
| NFR-1: MFA verify <= 200ms P95 | NFR | TC-AUTH-036 | Direct |
| NFR-3: Recovery codes shown once, never retrievable | NFR | TC-AUTH-035 | Direct |
| NFR-4: Rate limit 5 attempts per session | NFR | TC-AUTH-015, TC-AUTH-036 | Direct |
| BR-1: Per-tenant policy with per-role overrides | BR | TC-AUTH-029, TC-AUTH-032 | Direct |
| BR-2: Cross-tenant global MFA, per-tenant enforcement | BR | TC-AUTH-037 | Direct |
| BR-3: Cannot disable MFA when policy requires it | BR | TC-AUTH-034, TC-AUTH-037 | Direct |
| BR-4: Recovery codes are single-use | BR | TC-AUTH-030, TC-AUTH-031 | Direct |
| BR-5: Policy change prompts unenrolled users | BR | TC-AUTH-029, TC-AUTH-032 | Direct |

### US-AUTH-006 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| FR-1 (Module.Action.Scope pattern) | FR | TC-AUTH-039, TC-AUTH-043, TC-AUTH-045 | Direct |
| FR-2 (Tenant-scoped roles, built-in protection) | FR | TC-AUTH-040, TC-AUTH-041, TC-AUTH-ISO-004 | Direct |
| FR-3 (role_permission table) | FR | TC-AUTH-039, TC-AUTH-043, TC-AUTH-047 | Direct |
| FR-4 (user_tenant_role many-to-many) | FR | TC-AUTH-039, TC-AUTH-043, TC-AUTH-046, TC-AUTH-047 | Direct |
| FR-5 (Three-layer authorization) | FR | TC-AUTH-039, TC-AUTH-045, TC-AUTH-049 | Direct |
| FR-6 (CRUD endpoints) | FR | TC-AUTH-039, TC-AUTH-040, TC-AUTH-044, TC-AUTH-046, TC-AUTH-050 | Direct |
| FR-7 (Audit logging) | FR | TC-AUTH-050 | Direct |
| FR-8 (Tenant Owner protection) | FR | TC-AUTH-042 | Direct |
| FR-9 (System roles isolation) | FR | TC-AUTH-041 | Direct |
| FR-10 (EF Core filters + RLS) | FR | TC-AUTH-ISO-004 | Direct |
| NFR-1 (Permission eval <= 5ms) | NFR | TC-AUTH-049 | Direct |
| NFR-2 (Redis cache + invalidation) | NFR | TC-AUTH-047, TC-AUTH-ISO-004 | Direct |
| NFR-3 (50 roles / 200+ permissions) | NFR | TC-AUTH-044 | Direct |
| NFR-4 (Auth failure logging) | NFR | TC-AUTH-050 | Direct |
| BR-1 (Per-tenant-membership roles) | BR | TC-AUTH-018, TC-AUTH-ISO-004 | Direct |
| BR-2 (Built-in immutable) | BR | TC-AUTH-040 | Direct |
| BR-3 (Custom role permission subsets) | BR | TC-AUTH-039 | Direct |
| BR-4 (Permission union) | BR | TC-AUTH-043 | Direct |
| BR-5 (Effect on next token refresh) | BR | TC-AUTH-046 | Direct |
| BR-6 (Tenant Owner minimum one) | BR | TC-AUTH-042 | Direct |
| BR-7 (Delete role with users) | BR | TC-AUTH-046 | Direct |

### Coverage Summary

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 61/61 (100%) | >= 100% | PASS |
| US-AUTH-005 AC Coverage | 7/7 (100%) | >= 100% | PASS |
| US-AUTH-005 FR Coverage | 10/10 (100%) | >= 100% | PASS |
| US-AUTH-005 NFR Coverage | 3/3 covered (NFR-1, NFR-3, NFR-4) | >= 85% | PASS |
| US-AUTH-005 BR Coverage | 5/5 (100%) | >= 100% | PASS |
| US-AUTH-006 Requirement Coverage | 10/10 FR + 4/4 NFR + 7/7 BR = 100% | >= 85% | PASS |
| US-AUTH-007 Requirement Coverage | 10/10 FR + 5/5 NFR + 5/5 BR = 100% | >= 85% | PASS |
| US-AUTH-008 Requirement Coverage | 9/9 FR + 4/4 NFR + 5/5 BR = 100% | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 17 (4 dedicated + 13 embedded) | >= 3 | PASS |
| Security Test Cases | 29/68 (43%) | >= 30% | PASS |
| Critical Module Coverage | 100% | >= 85% | PASS |
| API Endpoint Coverage | 26/26 (100%) | >= 90% | PASS |

---

*Note: This traceability matrix will be extended as test cases for additional modules (Employee Management, Leave Management, Attendance, Payroll, etc.) are authored.*
