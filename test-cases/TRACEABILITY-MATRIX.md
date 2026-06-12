---
title: Requirements Traceability Matrix
project: HRM SaaS Platform
created: 2026-05-11
status: draft
last_updated: 2026-06-12
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
| US-AUTH-009 | Session management and concurrent limits | Should Have | TC-AUTH-024, TC-AUTH-025, TC-AUTH-065, TC-AUTH-066, TC-AUTH-067, TC-AUTH-068, TC-AUTH-069, TC-AUTH-070, TC-AUTH-071, TC-AUTH-072, TC-AUTH-073, TC-AUTH-074, TC-AUTH-075, TC-AUTH-076, TC-AUTH-077, TC-AUTH-078, TC-AUTH-079, TC-AUTH-080, TC-AUTH-081, TC-AUTH-082 | 20 | 6/6 AC covered (deep) |
| US-AUTH-010 | Account lockout after failed attempts | Must Have | TC-AUTH-026, TC-AUTH-027, TC-AUTH-028, TC-AUTH-083, TC-AUTH-084, TC-AUTH-085, TC-AUTH-086, TC-AUTH-087, TC-AUTH-088, TC-AUTH-089, TC-AUTH-090, TC-AUTH-091, TC-AUTH-092, TC-AUTH-093, TC-AUTH-094, TC-AUTH-095, TC-AUTH-096, TC-AUTH-097, TC-AUTH-098, TC-AUTH-099, TC-AUTH-100, TC-AUTH-101, TC-AUTH-102, TC-AUTH-103, TC-AUTH-104, TC-AUTH-105, TC-AUTH-106, TC-AUTH-107, TC-AUTH-108, TC-AUTH-109, TC-AUTH-110, TC-AUTH-111, TC-AUTH-112 | 33 | 6/6 AC covered (deep) |
| Cross-cutting | Multi-tenant isolation (mandatory) | Critical | TC-AUTH-ISO-001, TC-AUTH-ISO-002, TC-AUTH-ISO-003, TC-AUTH-ISO-004 | 4 | -- |
| **TOTAL** | | | **116 test cases** | **116** | **61/61 AC** |

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
| TC-AUTH-025 | Oldest session terminated when limit exceeded | Functional | High | US-AUTH-009 | AC-1, AC-4, AC-5, AC-6 |
| TC-AUTH-026 | Account locked after N failed attempts | Security | Critical | US-AUTH-010 | AC-1, AC-2 |
| TC-AUTH-027 | Locked account cannot login | Security | Critical | US-AUTH-010 | AC-3, FR-5, NFR-4, BR-7 |
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
| TC-AUTH-065 | Concurrent session limit -- deny_new strategy blocks login at limit | Functional | Critical | US-AUTH-009 | AC-1, FR-1, FR-5, BR-1 |
| TC-AUTH-066 | Concurrent session limit -- revoke_oldest strategy evicts oldest session | Functional | Critical | US-AUTH-009 | AC-1, FR-1, FR-5, FR-9, BR-1 |
| TC-AUTH-067 | Idle timeout expires session and returns 401 on refresh | Functional | Critical | US-AUTH-009 | AC-2, FR-1, FR-2, FR-4, FR-9, BR-1, BR-6 |
| TC-AUTH-068 | Absolute timeout forces re-authentication regardless of activity | Functional | Critical | US-AUTH-009 | AC-3, FR-1, FR-3, FR-9, BR-1 |
| TC-AUTH-069 | Admin views a user's active sessions with device, browser, IP, and timestamps | Functional | High | US-AUTH-009 | AC-4, FR-6, NFR-5, BR-1 |
| TC-AUTH-070 | Admin revokes a specific session and revokes all sessions for a user | Functional | Critical | US-AUTH-009 | AC-5, FR-8, FR-9, BR-1, BR-5 |
| TC-AUTH-071 | User views own sessions and revokes a non-current session | Functional | High | US-AUTH-009 | AC-6, FR-7, FR-8, FR-9, BR-4 |
| TC-AUTH-072 | User cannot revoke their own current session (BR-4) | Functional | High | US-AUTH-009 | AC-6, FR-8, BR-4 |
| TC-AUTH-073 | Negative -- revoke non-existent session and non-admin calls admin endpoint | Security | High | US-AUTH-009 | AC-5, AC-6, FR-6, FR-8 |
| TC-AUTH-074 | Boundary -- exactly at maxConcurrentSessions and timeout at exact threshold | Functional | High | US-AUTH-009 | AC-1, AC-2, AC-3, FR-1, FR-2, FR-3, FR-5, BR-1 |
| TC-AUTH-075 | Cross-tenant session isolation -- policies and sessions are tenant-scoped | Security | Critical | US-AUTH-009 | AC-1, AC-4, AC-5, FR-1, FR-5, FR-6, FR-8, NFR-5, BR-1 |
| TC-AUTH-076 | Session metadata is not exposed to other users (NFR-5) | Security | High | US-AUTH-009 | AC-4, AC-6, FR-6, FR-7, NFR-5 |
| TC-AUTH-077 | Session list P95 <= 200 ms and last_active_at update overhead <= 2 ms | Performance | High | US-AUTH-009 | AC-4, AC-6, FR-4, FR-6, FR-7, NFR-1, NFR-2, NFR-3 |
| TC-AUTH-078 | Audit trail records all session management event types | Security | High | US-AUTH-009 | AC-1, AC-2, AC-3, AC-5, AC-6, FR-9 |
| TC-AUTH-079 | Hangfire background job cleans up expired and revoked refresh tokens | Functional | High | US-AUTH-009 | FR-10 |
| TC-AUTH-080 | Session policy configuration via PUT /api/v1/tenant/auth-settings | Functional | High | US-AUTH-009 | AC-1, AC-2, AC-3, FR-1 |
| TC-AUTH-081 | Idle timeout is reset by any authenticated API request (BR-6) | Functional | High | US-AUTH-009 | AC-2, FR-2, FR-4, BR-6 |
| TC-AUTH-082 | System admin sessions follow system policy; impersonation sessions excluded from count | Security | High | US-AUTH-009 | AC-1, FR-1, FR-5, BR-2, BR-3 |
| TC-AUTH-083 | Failed login increment below threshold returns generic 401 with no remaining-count leak | Functional | Critical | US-AUTH-010 | AC-1, FR-1, FR-3, NFR-4 |
| TC-AUTH-084 | Lockout triggered at threshold -- locked_until set, lockout message, account_locked audit | Security | Critical | US-AUTH-010 | AC-2, FR-1, FR-2, FR-3, FR-7 |
| TC-AUTH-085 | Correct credentials during active lockout are still rejected | Security | Critical | US-AUTH-010 | AC-3, FR-5, NFR-4 |
| TC-AUTH-086 | Lockout expiry clears counters and allows successful login | Functional | Critical | US-AUTH-010 | AC-4, FR-2, FR-4 |
| TC-AUTH-087 | Admin manual unlock clears counters, logs audit, and enables immediate login | Functional | Critical | US-AUTH-010 | AC-5, FR-6, FR-7 |
| TC-AUTH-088 | Successful login below threshold resets failed_login_count to zero | Functional | Critical | US-AUTH-010 | AC-6, FR-1, FR-4 |
| TC-AUTH-089 | Progressive lockout doubles duration after repeated lockout cycles | Functional | Critical | US-AUTH-010 | FR-9 |
| TC-AUTH-090 | MFA failures count toward lockout threshold (shared counter) | Security | Critical | US-AUTH-010 | FR-10 |
| TC-AUTH-091 | Password reset clears lockout state | Functional | Critical | US-AUTH-010 | BR-2, FR-4 |
| TC-AUTH-092 | Global lockout blocks login on all tenants (cross-tenant lockout enforcement) | Security | Critical | US-AUTH-010 | BR-1, FR-1, FR-2, FR-5 |
| TC-AUTH-093 | Tenant admin can only unlock users with membership in their own tenant | Security | Critical | US-AUTH-010 | BR-3, FR-6 |
| TC-AUTH-094 | System admin can unlock any user regardless of tenant | Security | Critical | US-AUTH-010 | BR-4, FR-6, FR-7 |
| TC-AUTH-095 | Lockout does NOT revoke active sessions (existing refresh tokens remain valid) | Security | High | US-AUTH-010 | BR-7 |
| TC-AUTH-096 | Social login failures do NOT increment the lockout counter | Security | High | US-AUTH-010 | BR-6 |
| TC-AUTH-097 | Timing-attack resistance -- locked vs non-existent accounts have similar response times | Security | Critical | US-AUTH-010 | NFR-4, FR-5 |
| TC-AUTH-098 | Atomic increment of failed_login_count under concurrent login attempts | Security | Critical | US-AUTH-010 | NFR-2, FR-1 |
| TC-AUTH-099 | Lockout state persists across API instance restarts | Security | High | US-AUTH-010 | NFR-5 |
| TC-AUTH-100 | Lockout notification email sent within 60 seconds via Hangfire | Functional | High | US-AUTH-010 | FR-8, NFR-3 |
| TC-AUTH-101 | Audit trail -- all lockout-related event types are recorded with correct metadata | Security | High | US-AUTH-010 | FR-7 |
| TC-AUTH-102 | Lockout policy bounds -- maxFailedAttempts and lockoutDurationMinutes reject out-of-range values | Security | High | US-AUTH-010 | BR-5, FR-3 |
| TC-AUTH-103 | Custom tenant lockout policy is applied (non-default maxFailedAttempts and duration) | Functional | High | US-AUTH-010 | FR-3, BR-5 |
| TC-AUTH-104 | Lockout check overhead is within 2ms (NFR-1 performance) | Performance | High | US-AUTH-010 | NFR-1 |
| TC-AUTH-105 | Failed login attempts accumulate across tenants toward global lockout | Security | High | US-AUTH-010 | BR-1, FR-1, FR-2 |
| TC-AUTH-106 | Progressive lockout disabled -- duration remains constant across cycles | Functional | High | US-AUTH-010 | FR-9 |
| TC-AUTH-107 | Non-admin user cannot call the unlock endpoint | Security | High | US-AUTH-010 | FR-6, BR-3 |
| TC-AUTH-108 | Lockout UI displays correct error banner and admin UI shows locked badge | Functional | Medium | US-AUTH-010 | UI/UX Section 8 |
| TC-AUTH-109 | Lockout error banner and admin lockout UI meet WCAG 2.1 AA accessibility standards | Accessibility | Medium | US-AUTH-010 | UI/UX Section 8, WCAG 2.1 AA |
| TC-AUTH-110 | MFA-only failures trigger lockout (5 consecutive invalid TOTP codes) | Functional | High | US-AUTH-010 | FR-10 |
| TC-AUTH-111 | Lockout at exact boundary -- threshold minus one does not lock, threshold locks | Security | High | US-AUTH-010 | AC-1, AC-2, FR-1, FR-2 |
| TC-AUTH-112 | Unlock of an already-unlocked account is idempotent and non-destructive | Security | High | US-AUTH-010 | AC-5, FR-6 |
| TC-AUTH-ISO-001 | Tenant A user cannot authenticate as Tenant B | Security | Critical | US-AUTH-001, US-AUTH-007 | -- |
| TC-AUTH-ISO-002 | JWT claims include correct tenant_id | Security | Critical | US-AUTH-002, US-AUTH-006 | -- |
| TC-AUTH-ISO-003 | API rejects requests with mismatched tenant context | Security | Critical | US-AUTH-002, US-AUTH-007 | -- |
| TC-AUTH-ISO-004 | RBAC cross-tenant isolation -- roles, permissions, and cache keys are tenant-scoped | Security | Critical | US-AUTH-006 | FR-2, FR-10, NFR-2, BR-1 |

### US-AUTH-010 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Failed login below threshold increments counter, returns generic 401, no remaining-count leak | AC | TC-AUTH-026, TC-AUTH-083, TC-AUTH-111 | Direct |
| AC-2: Lockout at threshold sets locked_until, returns lockout message, logs account_locked audit | AC | TC-AUTH-026, TC-AUTH-084, TC-AUTH-111 | Direct |
| AC-3: Correct credentials during lockout are still rejected | AC | TC-AUTH-027, TC-AUTH-085 | Direct |
| AC-4: Lockout expiry clears counters and login succeeds | AC | TC-AUTH-028, TC-AUTH-086 | Direct |
| AC-5: Admin manual unlock clears counters, logs account_unlocked_by_admin, immediate login | AC | TC-AUTH-028, TC-AUTH-087, TC-AUTH-112 | Direct |
| AC-6: Successful login below threshold resets failed_login_count | AC | TC-AUTH-028, TC-AUTH-088 | Direct |
| FR-1: Track consecutive failed login attempts in failed_login_count | FR | TC-AUTH-026, TC-AUTH-083, TC-AUTH-084, TC-AUTH-088, TC-AUTH-098, TC-AUTH-105, TC-AUTH-111 | Direct |
| FR-2: Set locked_until to now + lockoutDuration on reaching max attempts | FR | TC-AUTH-026, TC-AUTH-084, TC-AUTH-086, TC-AUTH-089, TC-AUTH-092, TC-AUTH-105, TC-AUTH-111 | Direct |
| FR-3: Lockout policy configurable per tenant (maxFailedAttempts, lockoutDurationMinutes) | FR | TC-AUTH-026, TC-AUTH-083, TC-AUTH-084, TC-AUTH-102, TC-AUTH-103 | Direct |
| FR-4: On success reset failed_login_count to 0 and locked_until to null | FR | TC-AUTH-028, TC-AUTH-086, TC-AUTH-088, TC-AUTH-091 | Direct |
| FR-5: Check locked_until before verifying credentials | FR | TC-AUTH-027, TC-AUTH-085, TC-AUTH-092, TC-AUTH-097 | Direct |
| FR-6: Tenant admins can unlock accounts via user management | FR | TC-AUTH-028, TC-AUTH-087, TC-AUTH-093, TC-AUTH-094, TC-AUTH-107, TC-AUTH-112 | Direct |
| FR-7: Lockout and unlock events in tenant + system audit log | FR | TC-AUTH-026, TC-AUTH-084, TC-AUTH-087, TC-AUTH-094, TC-AUTH-101 | Direct |
| FR-8: Notification email on lockout | FR | TC-AUTH-026, TC-AUTH-100 | Direct |
| FR-9: Progressive lockout -- duration doubles after repeated cycles | FR | TC-AUTH-089, TC-AUTH-106 | Direct |
| FR-10: MFA failures count toward lockout threshold | FR | TC-AUTH-090, TC-AUTH-110 | Direct |
| NFR-1: Lockout check adds <= 2 ms overhead | NFR | TC-AUTH-104 | Direct |
| NFR-2: Atomic failed_login_count increment (database-level) | NFR | TC-AUTH-098 | Direct |
| NFR-3: Lockout notification sent within 60 seconds via Hangfire | NFR | TC-AUTH-100 | Direct |
| NFR-4: Timing-attack resistance (locked vs unlocked response time indistinguishable) | NFR | TC-AUTH-027, TC-AUTH-083, TC-AUTH-085, TC-AUTH-097 | Direct |
| NFR-5: Lockout state in database, persists across restarts | NFR | TC-AUTH-099 | Direct |
| BR-1: Lockout per global user account, blocks all tenants | BR | TC-AUTH-092, TC-AUTH-105 | Direct |
| BR-2: Password reset clears lockout | BR | TC-AUTH-091 | Direct |
| BR-3: Tenant admin can only unlock own-tenant users | BR | TC-AUTH-093, TC-AUTH-107 | Direct |
| BR-4: System admin can unlock any user | BR | TC-AUTH-094 | Direct |
| BR-5: Policy bounds (maxFailedAttempts 3-10, lockoutDurationMinutes 5-60) | BR | TC-AUTH-102, TC-AUTH-103 | Direct |
| BR-6: Social login failures do not increment counter | BR | TC-AUTH-096 | Direct |
| BR-7: Lockout does not revoke active sessions | BR | TC-AUTH-027, TC-AUTH-095 | Direct |

### US-AUTH-009 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Concurrent session limit enforced per strategy | AC | TC-AUTH-024, TC-AUTH-025, TC-AUTH-065, TC-AUTH-066, TC-AUTH-074, TC-AUTH-075, TC-AUTH-078, TC-AUTH-080, TC-AUTH-082 | Direct |
| AC-2: Idle timeout revokes refresh token and returns 401 | AC | TC-AUTH-024, TC-AUTH-067, TC-AUTH-074, TC-AUTH-075, TC-AUTH-078, TC-AUTH-080, TC-AUTH-081 | Direct |
| AC-3: Absolute timeout forces re-authentication | AC | TC-AUTH-024, TC-AUTH-068, TC-AUTH-074, TC-AUTH-078, TC-AUTH-080 | Direct |
| AC-4: Admin views active sessions with metadata | AC | TC-AUTH-025, TC-AUTH-069, TC-AUTH-075, TC-AUTH-076, TC-AUTH-077 | Direct |
| AC-5: Admin revokes specific or all sessions; audit logged; forced re-auth | AC | TC-AUTH-025, TC-AUTH-070, TC-AUTH-073, TC-AUTH-075, TC-AUTH-078 | Direct |
| AC-6: Self session view and self revoke; current session not revocable | AC | TC-AUTH-025, TC-AUTH-071, TC-AUTH-072, TC-AUTH-073, TC-AUTH-076, TC-AUTH-077, TC-AUTH-078 | Direct |
| FR-1: Session policy configurable per tenant via PUT /api/v1/tenant/auth-settings | FR | TC-AUTH-024, TC-AUTH-065, TC-AUTH-066, TC-AUTH-074, TC-AUTH-075, TC-AUTH-080, TC-AUTH-082 | Direct |
| FR-2: Refresh endpoint checks idle timeout via last_active_at | FR | TC-AUTH-067, TC-AUTH-074, TC-AUTH-081 | Direct |
| FR-3: Refresh endpoint checks absolute timeout via issued_at | FR | TC-AUTH-068, TC-AUTH-074 | Direct |
| FR-4: last_active_at updated on each authenticated request (debounced) | FR | TC-AUTH-067, TC-AUTH-077, TC-AUTH-081 | Direct |
| FR-5: Concurrent session check at login (count non-revoked, non-expired tokens) | FR | TC-AUTH-024, TC-AUTH-065, TC-AUTH-066, TC-AUTH-074, TC-AUTH-075, TC-AUTH-082 | Direct |
| FR-6: Admin sessions endpoint GET /api/v1/tenant/users/{id}/sessions | FR | TC-AUTH-069, TC-AUTH-073, TC-AUTH-075, TC-AUTH-076, TC-AUTH-077 | Direct |
| FR-7: Self sessions endpoint GET /api/v1/auth/me/sessions | FR | TC-AUTH-025, TC-AUTH-071, TC-AUTH-072, TC-AUTH-073, TC-AUTH-075, TC-AUTH-076, TC-AUTH-077 | Direct |
| FR-8: Session revocation endpoints (admin and self) | FR | TC-AUTH-025, TC-AUTH-070, TC-AUTH-071, TC-AUTH-072, TC-AUTH-073, TC-AUTH-076 | Direct |
| FR-9: All session management actions recorded in audit log | FR | TC-AUTH-065, TC-AUTH-066, TC-AUTH-067, TC-AUTH-068, TC-AUTH-070, TC-AUTH-071, TC-AUTH-078 | Direct |
| FR-10: Hangfire job cleans up expired/revoked tokens | FR | TC-AUTH-079 | Direct |
| NFR-1: last_active_at tracking adds <= 2 ms overhead | NFR | TC-AUTH-077 | Direct |
| NFR-2: Concurrent session counting performant with index | NFR | TC-AUTH-077 | Direct |
| NFR-3: Session list queries P95 <= 200 ms | NFR | TC-AUTH-077 | Direct |
| NFR-4: Clock drift handled gracefully (NTP, <= 1s) | NFR | TC-AUTH-074 | Direct |
| NFR-5: Session metadata only visible to owner and admins | NFR | TC-AUTH-069, TC-AUTH-075, TC-AUTH-076 | Direct |
| BR-1: Session policies are per-tenant | BR | TC-AUTH-065, TC-AUTH-066, TC-AUTH-067, TC-AUTH-068, TC-AUTH-075, TC-AUTH-080 | Direct |
| BR-2: System admin sessions follow system-level policies | BR | TC-AUTH-082 | Direct |
| BR-3: Impersonation sessions excluded from concurrent count | BR | TC-AUTH-082 | Direct |
| BR-4: Current session cannot be self-revoked | BR | TC-AUTH-072 | Direct |
| BR-5: Admin revocation triggers notification to affected user | BR | TC-AUTH-070 | Direct |
| BR-6: Idle timeout reset by any authenticated API request | BR | TC-AUTH-067, TC-AUTH-081 | Direct |

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
| US-AUTH-009 Requirement Coverage | 10/10 FR + 5/5 NFR + 6/6 BR = 100% | >= 85% | PASS |
| US-AUTH-010 Requirement Coverage | 10/10 FR + 5/5 NFR + 7/7 BR = 100% | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 23 (4 dedicated + 19 embedded) | >= 3 | PASS |
| Security Test Cases | 50/116 (43%) | >= 30% | PASS |
| Critical Module Coverage | 100% | >= 85% | PASS |
| API Endpoint Coverage | 31/31 (100%) | >= 90% | PASS |

---

## Core HR Module

### Forward Traceability (User Stories --> Test Cases)

| User Story ID | User Story Title | Priority | Test Cases | TC Count | Coverage |
|---------------|-----------------|----------|------------|----------|----------|
| US-CHR-001 | Add New Employee with Personal Information | Must Have | TC-CHR-064, TC-CHR-065, TC-CHR-066, TC-CHR-067, TC-CHR-068, TC-CHR-069, TC-CHR-070, TC-CHR-071, TC-CHR-072, TC-CHR-073, TC-CHR-074, TC-CHR-075, TC-CHR-076, TC-CHR-077, TC-CHR-078, TC-CHR-079, TC-CHR-080, TC-CHR-081, TC-CHR-082, TC-CHR-083, TC-CHR-084, TC-CHR-085, TC-CHR-086, TC-CHR-087, TC-CHR-088, TC-CHR-089, TC-CHR-090, TC-CHR-091, TC-CHR-092, TC-CHR-093, TC-CHR-094, TC-CHR-095, TC-CHR-096, TC-CHR-097, TC-CHR-098, TC-CHR-099, TC-CHR-100, TC-CHR-101, TC-CHR-102, TC-CHR-103 | 40 | 6/6 AC covered |
| US-CHR-002 | View and Edit Employee Profile | Must Have | TC-CHR-104, TC-CHR-105, TC-CHR-106, TC-CHR-107, TC-CHR-108, TC-CHR-109, TC-CHR-110, TC-CHR-111, TC-CHR-112, TC-CHR-113, TC-CHR-114, TC-CHR-115, TC-CHR-116, TC-CHR-117, TC-CHR-118, TC-CHR-119, TC-CHR-120, TC-CHR-121, TC-CHR-122, TC-CHR-123, TC-CHR-124, TC-CHR-125, TC-CHR-126 | 23 | 6/6 AC covered |
| US-CHR-003 | Employee Directory with Search and Filters | Must Have | TC-CHR-127, TC-CHR-128, TC-CHR-129, TC-CHR-130, TC-CHR-131, TC-CHR-132, TC-CHR-133, TC-CHR-134, TC-CHR-135, TC-CHR-136, TC-CHR-137, TC-CHR-138, TC-CHR-139, TC-CHR-140, TC-CHR-141, TC-CHR-142, TC-CHR-143, TC-CHR-144, TC-CHR-145, TC-CHR-146, TC-CHR-147, TC-CHR-148, TC-CHR-149, TC-CHR-150 | 24 | 5/5 AC covered |
| US-CHR-004 | Create and Manage Departments | Must Have | TC-CHR-001 through TC-CHR-034 | 34 | 5/5 AC covered (all unblocked) |
| US-CHR-005 | Create and Manage Job Titles and Positions | Must Have | TC-CHR-035 through TC-CHR-063 | 29 | 5/5 AC covered (all unblocked) |
| Cross-cutting (CHR-001) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-009, TC-CHR-ISO-010, TC-CHR-ISO-011, TC-CHR-ISO-012 | 4 | -- |
| Cross-cutting (CHR-002) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-013, TC-CHR-ISO-014, TC-CHR-ISO-015, TC-CHR-ISO-016 | 4 | -- |
| Cross-cutting (CHR-003) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-017, TC-CHR-ISO-018, TC-CHR-ISO-019, TC-CHR-ISO-020 | 4 | -- |
| Cross-cutting (CHR-004) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003, TC-CHR-ISO-004 | 4 | -- |
| Cross-cutting (CHR-005) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-005, TC-CHR-ISO-006, TC-CHR-ISO-007, TC-CHR-ISO-008 | 4 | -- |
| **TOTAL** | | | **170 test cases** | **170** | **27/27 AC** |

### Backward Traceability (Test Cases --> User Stories)

| Test Case ID | Test Case Title | Type | Priority | User Story | Requirements Covered |
|-------------|----------------|------|----------|------------|---------------------|
| TC-CHR-001 | Create a root department successfully (happy path) | Functional | Critical | US-CHR-004 | AC-1, AC-2, FR-1, FR-8, NFR-5, BR-4 |
| TC-CHR-002 | Create a child department with parent assignment | Functional | Critical | US-CHR-004 | AC-1, AC-2, AC-4, FR-1, FR-3, FR-8, BR-3, BR-4 |
| TC-CHR-003 | Reject duplicate department name within same tenant | Functional | Critical | US-CHR-004 | AC-3, FR-2, BR-1 |
| TC-CHR-004 | Same department name allowed in different tenants | Security | Critical | US-CHR-004 | AC-3, FR-2, NFR-2, BR-1 |
| TC-CHR-005 | Build multi-level department hierarchy (3+ levels) | Functional | Critical | US-CHR-004 | AC-2, AC-4, FR-3, FR-8, BR-3, BR-4 |
| TC-CHR-006 | Prevent circular parent-child reference (direct cycle) | Functional | Critical | US-CHR-004 | AC-4, FR-3, FR-5 |
| TC-CHR-007 | Prevent circular parent-child reference (indirect cycle A->B->C->A) | Functional | Critical | US-CHR-004 | AC-4, FR-3, FR-5 |
| TC-CHR-008 | Edit department name and description | Functional | High | US-CHR-004 | AC-4, FR-1, NFR-5 |
| TC-CHR-009 | Edit department parent (reassign in hierarchy) | Functional | High | US-CHR-004 | AC-4, FR-1, FR-3, FR-8, BR-3 |
| TC-CHR-010 | Deactivate department blocked when active employees assigned | Functional | Critical | US-CHR-004 | AC-5, FR-6, BR-5 |
| TC-CHR-011 | Deactivate department with no active employees (success) | Functional | High | US-CHR-004 | AC-5, FR-6, FR-7, NFR-5, BR-5 |
| TC-CHR-012 | Deactivate parent department blocked when active children exist | Functional | High | US-CHR-004 | AC-5, FR-6, BR-6 |
| TC-CHR-013 | Soft delete -- departments are never hard deleted | Functional | High | US-CHR-004 | FR-7, BR-5 |
| TC-CHR-014 | Unauthorized role (Employee) cannot create departments -- 403 | Security | Critical | US-CHR-004 | FR-1 |
| TC-CHR-015 | HR Officer role can manage departments | Security | High | US-CHR-004 | FR-1 |
| TC-CHR-016 | Unauthenticated request to department API returns 401 | Security | Critical | US-CHR-004 | FR-1 |
| TC-CHR-017 | Create department with empty required fields fails validation | Functional | High | US-CHR-004 | AC-1, AC-2, FR-1 |
| TC-CHR-018 | Department name boundary values (max length, whitespace, special chars) | Functional | High | US-CHR-004 | AC-2, FR-1, FR-2 |
| TC-CHR-019 | Input sanitization -- XSS and SQL injection in department name | Security | High | US-CHR-004 | FR-1, NFR-2 |
| TC-CHR-020 | Assign department manager (UNBLOCKED by US-CHR-001) | Functional | High | US-CHR-004 | AC-1, FR-4, BR-2 |
| TC-CHR-021 | Parent department must belong to the same tenant | Security | High | US-CHR-004 | AC-4, FR-3, BR-3, NFR-2 |
| TC-CHR-022 | Duplicate name check is case-insensitive within tenant | Functional | High | US-CHR-004 | AC-3, FR-2, BR-1 |
| TC-CHR-023 | Edit department name to an existing name is rejected | Functional | High | US-CHR-004 | AC-3, FR-1, FR-2, BR-1 |
| TC-CHR-024 | Department hierarchy depth tolerance (10 levels) | Functional | High | US-CHR-004 | FR-3, FR-8 |
| TC-CHR-025 | Tenant A cannot modify or deactivate Tenant B's departments | Security | Critical | US-CHR-004 | NFR-2, BR-1, BR-3 |
| TC-CHR-026 | Department CRUD API response time within SLA | Performance | High | US-CHR-004 | NFR-1 |
| TC-CHR-027 | Department page load within 2.5 seconds | Performance | High | US-CHR-004 | NFR-1, FR-8 |
| TC-CHR-028 | Support 500 departments per tenant without degradation | Performance | High | US-CHR-004 | NFR-4 |
| TC-CHR-029 | Department management UI accessibility (WCAG 2.1 AA) | Accessibility | Medium | US-CHR-004 | NFR-3 |
| TC-CHR-030 | Department management UI responsive design (360px to 1920px) | Functional | Medium | US-CHR-004 | NFR-3 |
| TC-CHR-031 | Audit log entries for department create, update, deactivate | Functional | High | US-CHR-004 | NFR-5 |
| TC-CHR-032 | TenantInterceptor auto-stamps tenant_id on new departments | Security | High | US-CHR-004 | AC-2, NFR-2 |
| TC-CHR-033 | Deactivated department hidden from dropdowns, visible in admin view | Functional | Medium | US-CHR-004 | FR-7, BR-5 |
| TC-CHR-034 | Cross-browser compatibility (Chrome, Edge, Firefox, Safari) | Functional | Medium | US-CHR-004 | NFR-3 |
| TC-CHR-035 | Job Titles list page displays correct columns and layout | Functional | Critical | US-CHR-005 | AC-1, FR-1, NFR-3, BR-4 |
| TC-CHR-036 | Create a new job title successfully (happy path) | Functional | Critical | US-CHR-005 | AC-1, AC-2, FR-1, FR-2, NFR-4, BR-1, BR-4 |
| TC-CHR-037 | Create job title with salary grade link | Functional | Critical | US-CHR-005 | AC-4, FR-1, FR-3, BR-2, BR-5 |
| TC-CHR-038 | Create job title without a linked grade | Functional | High | US-CHR-005 | AC-2, FR-1, FR-3, BR-2 |
| TC-CHR-039 | Edit job title name, description, and grade link | Functional | High | US-CHR-005 | AC-1, FR-1, FR-3, NFR-4, BR-2 |
| TC-CHR-040 | Deactivate job title with no assigned employees (success) | Functional | High | US-CHR-005 | AC-5, FR-1, FR-5, FR-7, NFR-4, BR-3 |
| TC-CHR-041 | Reject duplicate job title name within the same tenant | Functional | Critical | US-CHR-005 | AC-3, FR-2, BR-1 |
| TC-CHR-042 | Same job title name allowed in different tenants | Security | Critical | US-CHR-005 | AC-3, FR-2, NFR-2, BR-1 |
| TC-CHR-043 | Deactivate job title blocked when assigned to active employees (UNBLOCKED by US-CHR-001) | Functional | Critical | US-CHR-005 | AC-5, FR-7, BR-3 |
| TC-CHR-044 | Create job title with empty required fields fails validation | Functional | High | US-CHR-005 | AC-2, FR-1, FR-2 |
| TC-CHR-045 | Edit job title name to an existing name is rejected | Functional | High | US-CHR-005 | AC-3, FR-1, FR-2, BR-1 |
| TC-CHR-046 | Title name boundary values (max 150 chars, whitespace, special chars) | Functional | High | US-CHR-005 | AC-2, FR-1, FR-2 |
| TC-CHR-047 | Duplicate title name check is case-insensitive within tenant | Functional | High | US-CHR-005 | AC-3, FR-2, BR-1 |
| TC-CHR-048 | Deactivated job titles hidden from assignment dropdowns, visible in admin | Functional | High | US-CHR-005 | FR-5, BR-3 |
| TC-CHR-049 | Employee count badge displays correct count per job title (UNBLOCKED by US-CHR-001) | Functional | High | US-CHR-005 | AC-1, FR-4, BR-3 |
| TC-CHR-050 | Employment types reference entity supports defined values | Functional | High | US-CHR-005 | FR-6 |
| TC-CHR-051 | Unauthorized role (Employee) cannot manage job titles -- 403 | Security | Critical | US-CHR-005 | FR-1 |
| TC-CHR-052 | HR Officer role can manage job titles | Security | High | US-CHR-005 | FR-1 |
| TC-CHR-053 | Unauthenticated request to job titles API returns 401 | Security | Critical | US-CHR-005 | FR-1 |
| TC-CHR-054 | Input sanitization -- XSS and SQL injection in job title name | Security | High | US-CHR-005 | FR-1, NFR-2 |
| TC-CHR-055 | Tenant A cannot see or modify Tenant B's job titles | Security | Critical | US-CHR-005 | NFR-2, BR-1, BR-4 |
| TC-CHR-056 | Audit log entries for job title create, update, and deactivate | Functional | High | US-CHR-005 | NFR-4 |
| TC-CHR-057 | Job title CRUD API response time within SLA | Performance | High | US-CHR-005 | NFR-1 |
| TC-CHR-058 | Job titles page load within 2.5 seconds | Performance | High | US-CHR-005 | NFR-1, NFR-3 |
| TC-CHR-059 | Support large number of job titles per tenant without degradation | Performance | Medium | US-CHR-005 | NFR-1 |
| TC-CHR-060 | Job titles management UI accessibility (WCAG 2.1 AA) | Accessibility | Medium | US-CHR-005 | NFR-3 |
| TC-CHR-061 | Job titles management UI responsive design (360px to 1920px) | Functional | Medium | US-CHR-005 | NFR-3 |
| TC-CHR-062 | Cross-browser compatibility (Chrome, Edge, Firefox, Safari) | Functional | Medium | US-CHR-005 | NFR-3 |
| TC-CHR-063 | Grade linked to job title displayed on employee profile (UNBLOCKED by US-CHR-001) | Functional | High | US-CHR-005 | AC-4, FR-3, BR-5 |
| TC-CHR-064 | Multi-step wizard renders all sections (AC-1) | Functional | Critical | US-CHR-001 | AC-1, FR-1 |
| TC-CHR-065 | Create employee with all mandatory fields -- happy path (AC-2) | Functional | Critical | US-CHR-001 | AC-2, FR-2, FR-4, FR-7, BR-1, BR-3 |
| TC-CHR-066 | Employee_no auto-generation pattern and per-tenant sequence isolation | Functional | Critical | US-CHR-001 | AC-2, FR-2, BR-1 |
| TC-CHR-067 | Duplicate email in same tenant rejected (AC-3) | Functional | Critical | US-CHR-001 | AC-3, FR-3, BR-2 |
| TC-CHR-068 | Same email allowed in different tenant (AC-3, BR-2) | Security | Critical | US-CHR-001 | AC-3, FR-3, BR-2, NFR-2 |
| TC-CHR-069 | Profile photo upload with EXIF stripping and signed URL (AC-4) | Functional | Critical | US-CHR-001 | AC-4, FR-6 |
| TC-CHR-070 | Profile photo oversized (>5 MB) rejected | Functional | High | US-CHR-001 | AC-4, FR-6 |
| TC-CHR-071 | Profile photo disallowed MIME type (.exe) rejected | Security | High | US-CHR-001 | AC-4, FR-6 |
| TC-CHR-072 | Malware scan seam invoked on photo upload (NFR-3) | Security | High | US-CHR-001 | AC-4, FR-6, NFR-3 |
| TC-CHR-073 | Profile photo tenant-isolated storage path | Security | Critical | US-CHR-001 | AC-4, FR-6, NFR-2 |
| TC-CHR-074 | Plan employee limit reached -- creation blocked (AC-5) | Functional | Critical | US-CHR-001 | AC-5, FR-5 |
| TC-CHR-075 | Custom fields persisted to JSONB and shown on profile (AC-6) | Functional | Critical | US-CHR-001 | AC-6, FR-9 |
| TC-CHR-076 | Employee_no configurable pattern per tenant | Functional | High | US-CHR-001 | AC-2, FR-2, BR-1 |
| TC-CHR-077 | Date of joining not more than 90 days in the future (BR-4) | Functional | High | US-CHR-001 | BR-4 |
| TC-CHR-078 | Date of birth age validation -- must be >= 16 years old | Functional | High | US-CHR-001 | Data Requirements |
| TC-CHR-079 | Default status "active" and explicit "probation" (BR-3) | Functional | High | US-CHR-001 | BR-3 |
| TC-CHR-080 | Soft delete -- is_deleted flag, never hard deleted (BR-6) | Functional | High | US-CHR-001 | BR-6 |
| TC-CHR-081 | Audit columns auto-populated on employee creation (FR-7) | Functional | High | US-CHR-001 | FR-7 |
| TC-CHR-082 | Optional user_id linking for self-service portal (FR-8) | Functional | Medium | US-CHR-001 | FR-8 |
| TC-CHR-083 | Tenant_id set from session context, not from user input (FR-4) | Security | Critical | US-CHR-001 | FR-4, NFR-2 |
| TC-CHR-084 | Dynamic custom fields rendering per tenant configuration (FR-9) | Functional | High | US-CHR-001 | AC-6, FR-9 |
| TC-CHR-085 | Create employee with missing mandatory fields fails validation | Functional | High | US-CHR-001 | AC-2 |
| TC-CHR-086 | Invalid email format rejected | Functional | High | US-CHR-001 | FR-3 |
| TC-CHR-087 | Department_id and job_title_id must exist in tenant | Functional | High | US-CHR-001 | NFR-2 |
| TC-CHR-088 | First name and last name boundary values (min 1, max 100) | Functional | High | US-CHR-001 | Data Requirements |
| TC-CHR-089 | Email field boundary value (max 150 chars) | Functional | Medium | US-CHR-001 | Data Requirements |
| TC-CHR-090 | Invalid employment_type value rejected | Functional | High | US-CHR-001 | Data Requirements |
| TC-CHR-091 | Unauthenticated request to employee API returns 401 | Security | Critical | US-CHR-001 | Auth dependency |
| TC-CHR-092 | Unauthorized role cannot create employees -- 403 | Security | Critical | US-CHR-001 | Auth dependency |
| TC-CHR-093 | Input sanitization -- XSS and SQL injection in employee fields | Security | High | US-CHR-001 | NFR-2 |
| TC-CHR-094 | CSRF protection on employee creation endpoint | Security | High | US-CHR-001 | Security |
| TC-CHR-095 | Employee creation API response time within SLA (NFR-1) | Performance | High | US-CHR-001 | NFR-1 |
| TC-CHR-096 | Employee form page load within 2.5 seconds | Performance | High | US-CHR-001 | NFR-4 |
| TC-CHR-097 | WCAG 2.1 AA accessibility -- keyboard navigation and screen reader | Accessibility | Medium | US-CHR-001 | NFR-5 |
| TC-CHR-098 | Responsive design -- form at 360px, 768px, 1024px, 1920px | Functional | Medium | US-CHR-001 | NFR-4 |
| TC-CHR-099 | Cross-browser compatibility (Chrome, Edge, Firefox, Safari) | Functional | Medium | US-CHR-001 | NFR-4, NFR-5 |
| TC-CHR-100 | PII fields audit trail on access (NFR-6) | Security | High | US-CHR-001 | NFR-6 |
| TC-CHR-101 | Wizard Save as Draft and Save & Continue functionality | Functional | High | US-CHR-001 | UI/UX Section 8 |
| TC-CHR-102 | Emergency contact recommended but not mandatory (BR-5) | Functional | Medium | US-CHR-001 | BR-5 |
| TC-CHR-103 | Gender field accepts defined values including Prefer Not To Say | Functional | Medium | US-CHR-001 | Data Requirements |
| TC-CHR-104 | HR Officer views full employee profile -- all sections render (happy path) | Functional | Critical | US-CHR-002 | AC-1, FR-1, FR-7, NFR-4, BR-2 |
| TC-CHR-105 | HR Officer edits a profile section -- save succeeds with audit trail (happy path) | Functional | Critical | US-CHR-002 | AC-2, FR-2, FR-4, FR-5, BR-2 |
| TC-CHR-106 | Employee views own profile via self-service portal (happy path) | Functional | Critical | US-CHR-002 | AC-4, FR-3, BR-1 |
| TC-CHR-107 | Employee attempts to PATCH a restricted field (salary) -- expects 403 | Security | Critical | US-CHR-002 | AC-5, FR-3, BR-1 |
| TC-CHR-108 | Concurrency conflict -- second save with stale xmin rejected | Functional | Critical | US-CHR-002 | AC-3, FR-4 |
| TC-CHR-109 | Employment history timeline records sequential department and job title changes | Functional | High | US-CHR-002 | AC-6, FR-6, BR-4 |
| TC-CHR-110 | Manager views direct report profile in read-only mode | Functional | High | US-CHR-002 | AC-1, FR-3, BR-3 |
| TC-CHR-111 | Employee cannot edit HR-only fields via API (field-level authorization) | Security | High | US-CHR-002 | AC-5, FR-3 |
| TC-CHR-112 | Editing at field length limits -- boundary test for profile fields | Functional | High | US-CHR-002 | AC-2, FR-2, FR-3 |
| TC-CHR-113 | Multi-tenant isolation -- fetch employee from another tenant returns 404 | Security | Critical | US-CHR-002 | FR-7, NFR-3, BR-2 |
| TC-CHR-114 | Manager cannot edit direct report's profile -- read-only enforced | Security | High | US-CHR-002 | FR-3, BR-3 |
| TC-CHR-115 | Employee cannot view another employee's profile -- access denied | Security | High | US-CHR-002 | FR-3, BR-1 |
| TC-CHR-116 | Input sanitization -- XSS and SQL injection in profile edit fields | Security | High | US-CHR-002 | FR-2, FR-3 |
| TC-CHR-117 | Unauthenticated request to employee profile API returns 401 | Security | Critical | US-CHR-002 | FR-3, FR-7 |
| TC-CHR-118 | PII access recorded in audit log when viewing employee profile | Security | High | US-CHR-002 | NFR-4 |
| TC-CHR-119 | Employee profile page load within 2.5 seconds P95 (NFR-1) | Performance | High | US-CHR-002 | NFR-1 |
| TC-CHR-120 | Employee profile API read response time within 400ms P95 (NFR-2) | Performance | High | US-CHR-002 | NFR-2 |
| TC-CHR-121 | WCAG 2.1 AA -- edit buttons have accessible labels, screen reader announces section headings | Accessibility | Medium | US-CHR-002 | NFR-6 |
| TC-CHR-122 | Responsive design -- profile page at 360px, 768px, and 1440px | Functional | Medium | US-CHR-002 | NFR-5 |
| TC-CHR-123 | Employee edits permitted fields (phone, email, address, emergency contacts) -- self-service happy path | Functional | High | US-CHR-002 | AC-4, FR-2, FR-5, BR-1 |
| TC-CHR-124 | HR Officer changes department -- employment history entry and reporting structure update | Functional | High | US-CHR-002 | AC-6, FR-6, BR-4 |
| TC-CHR-125 | Soft-deleted employee not visible in normal views; accessible via Archived filter | Functional | Medium | US-CHR-002 | BR-6 |
| TC-CHR-126 | Cross-browser compatibility for employee profile page | Functional | Medium | US-CHR-002 | NFR-5 |
| TC-CHR-127 | Employee directory loads paginated, sorted by name ascending (happy path) | Functional | Critical | US-CHR-003 | AC-1, AC-4, FR-4, FR-5, FR-7 |
| TC-CHR-128 | Search directory by partial name, email, and employee_no (happy path) | Functional | Critical | US-CHR-003 | AC-2, FR-1, BR-5 |
| TC-CHR-129 | Filter by department and status with chips and URL params (happy path) | Functional | Critical | US-CHR-003 | AC-3, FR-2, FR-6 |
| TC-CHR-130 | Search with no matches displays empty state | Functional | High | US-CHR-003 | AC-2, FR-1 |
| TC-CHR-131 | Invalid and out-of-range page parameter handled gracefully | Functional | High | US-CHR-003 | AC-4, FR-5 |
| TC-CHR-132 | Pagination boundary -- 55 employees at page size 20 yields 3 pages | Functional | High | US-CHR-003 | AC-4, FR-5 |
| TC-CHR-133 | Export filtered employee list as CSV with correct columns and row count | Functional | Critical | US-CHR-003 | AC-5, FR-8, BR-4 |
| TC-CHR-134 | Export employee directory as Excel (.xlsx) with correct formatting | Functional | High | US-CHR-003 | AC-5, FR-8 |
| TC-CHR-135 | View mode toggle between card/grid and table/list view | Functional | High | US-CHR-003 | FR-3 |
| TC-CHR-136 | Sort by date of joining descending and other sort options | Functional | High | US-CHR-003 | FR-4, FR-6 |
| TC-CHR-137 | Show Archived toggle includes soft-deleted employees (BR-1) | Functional | High | US-CHR-003 | BR-1, FR-7 |
| TC-CHR-138 | Role-based field visibility -- Employee role sees basic directory only (FR-9, BR-3) | Security | Critical | US-CHR-003 | FR-9, BR-3, BR-4 |
| TC-CHR-139 | Manager sees only reporting chain (BR-2) -- deferred but visibility tier applied | Security | Critical | US-CHR-003 | BR-2, FR-9 |
| TC-CHR-140 | Export respects role-based visibility (BR-4) | Security | High | US-CHR-003 | BR-4, FR-8, FR-9 |
| TC-CHR-141 | Unauthenticated request to directory API returns 401 | Security | Critical | US-CHR-003 | FR-7, NFR-3 |
| TC-CHR-142 | XSS and SQL injection in search and filter parameters | Security | High | US-CHR-003 | FR-1, FR-2, NFR-3 |
| TC-CHR-143 | Directory page load within 2.5 seconds P95 at 5,000 employees (NFR-1) | Performance | Critical | US-CHR-003 | NFR-1 |
| TC-CHR-144 | Search results update within 500ms of user stopping typing (NFR-2) | Performance | High | US-CHR-003 | NFR-2 |
| TC-CHR-145 | Export 10,000 rows completes within 5 minutes or is async (NFR-5) | Performance | High | US-CHR-003 | NFR-5 |
| TC-CHR-146 | WCAG 2.1 AA keyboard navigation for filters and pagination (NFR-6) | Accessibility | Medium | US-CHR-003 | NFR-6 |
| TC-CHR-147 | Responsive grid reflow from 4 columns to 1 column (NFR-4) | Functional | Medium | US-CHR-003 | NFR-4 |
| TC-CHR-148 | Cross-browser compatibility (Chrome, Edge, Firefox, Safari) | Functional | Medium | US-CHR-003 | NFR-4 |
| TC-CHR-149 | Multi-filter combination -- department + status + job title + employment type | Functional | High | US-CHR-003 | AC-3, FR-2, FR-6 |
| TC-CHR-150 | URL state restoration for deep-linking and browser back/forward (FR-6) | Functional | High | US-CHR-003 | FR-6 |
| TC-CHR-ISO-001 | Tenant A cannot see Tenant B's departments | Security | Critical | US-CHR-004 | NFR-2, BR-1 |
| TC-CHR-ISO-002 | API rejects department requests without valid tenant context | Security | Critical | US-CHR-004 | NFR-2 |
| TC-CHR-ISO-003 | RLS blocks direct DB queries across tenants for departments | Security | Critical | US-CHR-004 | NFR-2, BR-1, BR-3 |
| TC-CHR-ISO-004 | Cache keys for departments are tenant-scoped | Security | Critical | US-CHR-004 | NFR-2 |
| TC-CHR-ISO-005 | Tenant A cannot see Tenant B's job titles | Security | Critical | US-CHR-005 | NFR-2, BR-1, BR-4 |
| TC-CHR-ISO-006 | API rejects job title requests without valid tenant context | Security | Critical | US-CHR-005 | NFR-2 |
| TC-CHR-ISO-007 | RLS blocks direct DB queries across tenants for job titles | Security | Critical | US-CHR-005 | NFR-2, BR-1, BR-4 |
| TC-CHR-ISO-008 | Cache keys for job titles are tenant-scoped | Security | Critical | US-CHR-005 | NFR-2 |
| TC-CHR-ISO-009 | Tenant A cannot see Tenant B's employees | Security | Critical | US-CHR-001 | NFR-2, BR-1, BR-2 |
| TC-CHR-ISO-010 | API rejects employee requests without valid tenant context | Security | Critical | US-CHR-001 | NFR-2, FR-4 |
| TC-CHR-ISO-011 | RLS blocks direct DB queries across tenants for employees | Security | Critical | US-CHR-001 | NFR-2 |
| TC-CHR-ISO-012 | Cache keys for employees are tenant-scoped | Security | Critical | US-CHR-001 | NFR-2 |
| TC-CHR-ISO-013 | Tenant A cannot view or edit Tenant B's employee profiles | Security | Critical | US-CHR-002 | FR-7, NFR-3 |
| TC-CHR-ISO-014 | API rejects employee profile requests without valid tenant context | Security | Critical | US-CHR-002 | FR-7, NFR-3 |
| TC-CHR-ISO-015 | RLS blocks direct DB queries across tenants for employee profiles | Security | Critical | US-CHR-002 | FR-7, NFR-3 |
| TC-CHR-ISO-016 | Cache keys for employee profiles are tenant-scoped | Security | Critical | US-CHR-002 | NFR-3 |
| TC-CHR-ISO-017 | Tenant A directory shows zero Tenant B employees | Security | Critical | US-CHR-003 | FR-7, NFR-3 |
| TC-CHR-ISO-018 | API rejects directory requests without valid tenant context | Security | Critical | US-CHR-003 | FR-7, NFR-3 |
| TC-CHR-ISO-019 | RLS blocks direct DB queries across tenants for directory data | Security | Critical | US-CHR-003 | FR-7, NFR-3, BR-1 |
| TC-CHR-ISO-020 | Cache keys for directory queries are tenant-scoped | Security | Critical | US-CHR-003 | FR-7, NFR-3 |

### US-CHR-003 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Paginated card/grid directory sorted by name ascending | AC | TC-CHR-127, TC-CHR-135, TC-CHR-146, TC-CHR-147 | Direct |
| AC-2: Search by partial name, email, employee_no, phone with 300ms debounce | AC | TC-CHR-128, TC-CHR-130, TC-CHR-142, TC-CHR-144 | Direct |
| AC-3: Filter by department + status with chips and URL params | AC | TC-CHR-129, TC-CHR-149, TC-CHR-150 | Direct |
| AC-4: Paginated results (default 20/page) with page controls and total count | AC | TC-CHR-127, TC-CHR-131, TC-CHR-132, TC-CHR-143 | Direct |
| AC-5: Export filtered list as CSV or Excel with matching columns, tenant-scoped | AC | TC-CHR-133, TC-CHR-134, TC-CHR-140, TC-CHR-145 | Direct |
| FR-1: Full-text search across name, email, employee_no, phone | FR | TC-CHR-128, TC-CHR-130, TC-CHR-142 | Direct |
| FR-2: Filter controls for department, job title, status, employment type, location, date of joining | FR | TC-CHR-129, TC-CHR-149 | Direct |
| FR-3: Two view modes: card/grid and table/list | FR | TC-CHR-135 | Direct |
| FR-4: Sorting by name, employee_no, date of joining, department | FR | TC-CHR-127, TC-CHR-136 | Direct |
| FR-5: Pagination with configurable page sizes (10, 20, 50) | FR | TC-CHR-127, TC-CHR-131, TC-CHR-132 | Direct |
| FR-6: Filter/search state persisted in URL query parameters | FR | TC-CHR-129, TC-CHR-136, TC-CHR-150 | Direct |
| FR-7: All queries scoped by tenant_id automatically | FR | TC-CHR-127, TC-CHR-137, TC-CHR-141, TC-CHR-ISO-017, TC-CHR-ISO-018, TC-CHR-ISO-019, TC-CHR-ISO-020 | Direct |
| FR-8: CSV and Excel export of filtered dataset | FR | TC-CHR-133, TC-CHR-134, TC-CHR-140 | Direct |
| FR-9: Role-based visibility (Manager: team; Employee: basic fields) | FR | TC-CHR-138, TC-CHR-139, TC-CHR-140 | Direct |
| NFR-1: Directory page load within 2.5s P95 for 5,000 employees | NFR | TC-CHR-143 | Direct |
| NFR-2: Search results update within 500ms | NFR | TC-CHR-144 | Direct |
| NFR-3: All queries tenant-isolated via RLS and EF Core | NFR | TC-CHR-ISO-017, TC-CHR-ISO-018, TC-CHR-ISO-019, TC-CHR-ISO-020 | Direct |
| NFR-4: Fully responsive; mobile defaults to card view | NFR | TC-CHR-147 | Direct |
| NFR-5: Export 10,000 rows within 5 minutes | NFR | TC-CHR-145 | Direct |
| NFR-6: WCAG 2.1 AA keyboard navigation for filters and pagination | NFR | TC-CHR-146 | Direct |
| BR-1: Soft-deleted excluded by default; HR can toggle Show Archived | BR | TC-CHR-137, TC-CHR-ISO-019 | Direct |
| BR-2: Managers see only reporting chain (deferred) | BR | TC-CHR-139 | Direct (deferred scope) |
| BR-3: Employees see simplified directory | BR | TC-CHR-138 | Direct |
| BR-4: Export respects role-based field visibility | BR | TC-CHR-133, TC-CHR-140 | Direct |
| BR-5: Search uses PostgreSQL full-text search (tsvector deferred; ILIKE implemented) | BR | TC-CHR-128, TC-CHR-143 | Indirect |

### US-CHR-002 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Comprehensive profile displayed in card-based sections | AC | TC-CHR-104, TC-CHR-106, TC-CHR-110, TC-CHR-121, TC-CHR-122 | Direct |
| AC-2: Edit section, save with xmin concurrency, audit log before/after, success toast | AC | TC-CHR-105, TC-CHR-108, TC-CHR-112, TC-CHR-123 | Direct |
| AC-3: Concurrency conflict detection -- stale xmin rejected, first change preserved | AC | TC-CHR-108 | Direct |
| AC-4: Employee self-service: full read-only except permitted editable fields | AC | TC-CHR-106, TC-CHR-123 | Direct |
| AC-5: Employee cannot edit restricted fields; API rejects PATCH with 403 | AC | TC-CHR-107, TC-CHR-111 | Direct |
| AC-6: Department/job title change records employment history and updates reporting structure | AC | TC-CHR-109, TC-CHR-124 | Direct |
| FR-1: Profile in card-based sections, each independently collapsible | FR | TC-CHR-104, TC-CHR-121 | Direct |
| FR-2: Inline editing (click-to-edit) or modal edit form per section | FR | TC-CHR-105, TC-CHR-112, TC-CHR-116, TC-CHR-123 | Direct |
| FR-3: Field-level permissions by role (HR: all; Employee: limited; Manager: read-only) | FR | TC-CHR-106, TC-CHR-107, TC-CHR-110, TC-CHR-111, TC-CHR-114, TC-CHR-115 | Direct |
| FR-4: Optimistic concurrency via PostgreSQL xmin column | FR | TC-CHR-105, TC-CHR-108 | Direct |
| FR-5: Audit log with before/after JSONB snapshots for every field change | FR | TC-CHR-105, TC-CHR-118, TC-CHR-123, TC-CHR-124 | Direct |
| FR-6: Employment history timeline with department, title, status, manager changes | FR | TC-CHR-109, TC-CHR-124 | Direct |
| FR-7: All queries scoped by tenant_id via EF Core global filter and RLS | FR | TC-CHR-113, TC-CHR-117, TC-CHR-ISO-013, TC-CHR-ISO-014, TC-CHR-ISO-015, TC-CHR-ISO-016 | Direct |
| NFR-1: Profile page load within 2.5s P95 on 4G | NFR | TC-CHR-119 | Direct |
| NFR-2: API read response time <= 400ms P95 | NFR | TC-CHR-120 | Direct |
| NFR-3: Tenant-isolated via PostgreSQL RLS and EF Core query filters | NFR | TC-CHR-113, TC-CHR-ISO-013, TC-CHR-ISO-014, TC-CHR-ISO-015, TC-CHR-ISO-016 | Direct |
| NFR-4: PII access recorded in audit log | NFR | TC-CHR-118 | Direct |
| NFR-5: Fully responsive (360px to 4K) | NFR | TC-CHR-122, TC-CHR-126 | Direct |
| NFR-6: WCAG 2.1 AA standards | NFR | TC-CHR-121 | Direct |
| BR-1: Employees can only view/edit their own profile | BR | TC-CHR-106, TC-CHR-107, TC-CHR-115, TC-CHR-123 | Direct |
| BR-2: HR Officers can view/edit any employee within their tenant | BR | TC-CHR-104, TC-CHR-105, TC-CHR-113, TC-CHR-118 | Direct |
| BR-3: Managers read-only for direct reports | BR | TC-CHR-110, TC-CHR-114 | Direct |
| BR-4: Employment-critical field changes trigger history entry | BR | TC-CHR-109, TC-CHR-124 | Direct |
| BR-5: Profile edits on sensitive fields may require HR approval (configurable) | BR | -- | Deferred (configurable per tenant; not yet implemented) |
| BR-6: Soft-deleted employees hidden in normal views, accessible via Archived filter | BR | TC-CHR-125 | Direct |

### US-CHR-001 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Multi-step wizard with all sections | AC | TC-CHR-064, TC-CHR-097, TC-CHR-098, TC-CHR-101 | Direct |
| AC-2: Create with mandatory fields, status active, auto employee_no, tenant_id from session | AC | TC-CHR-065, TC-CHR-066, TC-CHR-076, TC-CHR-079, TC-CHR-081, TC-CHR-083, TC-CHR-085 | Direct |
| AC-3: Duplicate email same tenant rejected; same email allowed cross-tenant | AC | TC-CHR-067, TC-CHR-068 | Direct |
| AC-4: Profile photo upload, tenant-isolated storage, EXIF stripped, signed URL | AC | TC-CHR-069, TC-CHR-070, TC-CHR-071, TC-CHR-072, TC-CHR-073 | Direct |
| AC-5: Plan employee limit reached, creation blocked | AC | TC-CHR-074 | Direct |
| AC-6: Custom fields persisted to JSONB and shown on profile | AC | TC-CHR-075, TC-CHR-084 | Direct |
| FR-1: Multi-step form with all sections | FR | TC-CHR-064, TC-CHR-097 | Direct |
| FR-2: Auto-generate unique employee_no with configurable pattern | FR | TC-CHR-065, TC-CHR-066, TC-CHR-076 | Direct |
| FR-3: Email uniqueness within tenant scope | FR | TC-CHR-067, TC-CHR-068, TC-CHR-086 | Direct |
| FR-4: tenant_id from session context, never from user input | FR | TC-CHR-065, TC-CHR-083, TC-CHR-ISO-010 | Direct |
| FR-5: Plan-level employee count limits enforced | FR | TC-CHR-074 | Direct |
| FR-6: Profile photo upload with MIME validation, max 5 MB, EXIF stripping | FR | TC-CHR-069, TC-CHR-070, TC-CHR-071, TC-CHR-072, TC-CHR-073 | Direct |
| FR-7: Audit columns auto-populated | FR | TC-CHR-081 | Direct |
| FR-8: Optional user_id FK for self-service portal | FR | TC-CHR-082 | Direct |
| FR-9: Tenant-configured custom fields rendered dynamically | FR | TC-CHR-075, TC-CHR-084 | Direct |
| NFR-1: Employee creation API response <= 800 ms (P95) | NFR | TC-CHR-095 | Direct |
| NFR-2: All employee data tenant-isolated via RLS and EF global query filters | NFR | TC-CHR-068, TC-CHR-073, TC-CHR-083, TC-CHR-087, TC-CHR-093, TC-CHR-ISO-009, TC-CHR-ISO-010, TC-CHR-ISO-011, TC-CHR-ISO-012 | Direct |
| NFR-3: Profile photo scanned for malware (ClamAV) | NFR | TC-CHR-072 | Direct |
| NFR-4: Form fully responsive from 360px to 4K | NFR | TC-CHR-096, TC-CHR-098, TC-CHR-099 | Direct |
| NFR-5: WCAG 2.1 AA accessibility standards | NFR | TC-CHR-097 | Direct |
| NFR-6: PII fields logged in audit trail when accessed | NFR | TC-CHR-100 | Direct |
| BR-1: employee_no unique within tenant, may repeat cross-tenant | BR | TC-CHR-065, TC-CHR-066, TC-CHR-076 | Direct |
| BR-2: email unique within tenant, may repeat cross-tenant | BR | TC-CHR-067, TC-CHR-068 | Direct |
| BR-3: Default status "active" unless explicitly "probation" | BR | TC-CHR-079 | Direct |
| BR-4: date_of_joining not > 90 days in the future | BR | TC-CHR-077 | Direct |
| BR-5: Emergency contact recommended but not mandatory | BR | TC-CHR-102 | Direct |
| BR-6: Soft delete (is_deleted = true); never hard-deleted via UI | BR | TC-CHR-080 | Direct |

### Coverage Summary (Core HR -- US-CHR-003)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 9/9 (100%) | >= 85% | PASS |
| Non-Functional Requirements Coverage | 6/6 (100%) | >= 85% | PASS |
| Business Rules Coverage | 5/5 (100%) -- BR-2 scope deferred, BR-5 search ILIKE | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 5 (4 dedicated ISO + 1 embedded) | >= 3 | PASS |
| Security Test Cases | 9/28 (32.1%) | >= 30% | PASS |
| Performance Test Cases | 3/28 | >= 1 | PASS |
| Accessibility Test Cases | 1/28 | >= 1 | PASS |
| Cross-Browser Test Cases | 2/28 | >= 1 | PASS |

### Coverage Summary (Core HR -- US-CHR-002)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 6/6 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 7/7 (100%) | >= 85% | PASS |
| Non-Functional Requirements Coverage | 6/6 (100%) | >= 85% | PASS |
| Business Rules Coverage | 5/6 (83.3%) -- BR-5 deferred (configurable approval) | >= 85% | NOTE |
| Multi-Tenant Isolation Tests | 5 (4 dedicated ISO + 1 embedded) | >= 3 | PASS |
| Security Test Cases | 13/27 (48.1%) | >= 30% | PASS |
| Performance Test Cases | 2/27 | >= 1 | PASS |
| Accessibility Test Cases | 1/27 | >= 1 | PASS |
| Cross-Browser Test Cases | 2/27 | >= 1 | PASS |

### Coverage Summary (Core HR -- US-CHR-001)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 6/6 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 9/9 (100%) | >= 85% | PASS |
| Non-Functional Requirements Coverage | 6/6 (100%) | >= 85% | PASS |
| Business Rules Coverage | 6/6 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 11 (4 dedicated ISO + 7 embedded) | >= 3 | PASS |
| Security Test Cases | 15/44 (34.1%) | >= 30% | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |

### Coverage Summary (Core HR -- US-CHR-004)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 8/8 (100%) -- FR-4 now unblocked | >= 85% | PASS |
| Non-Functional Requirements Coverage | 5/5 (100%) | >= 85% | PASS |
| Business Rules Coverage | 6/6 (100%) -- BR-2 now unblocked | >= 85% | PASS |
| Blocked Test Cases | 0 (TC-CHR-020 unblocked by US-CHR-001) | -- | CLEAR |

### Coverage Summary (Core HR -- US-CHR-005)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 7/7 (100%) -- FR-4 and FR-7 now unblocked | >= 85% | PASS |
| Non-Functional Requirements Coverage | 4/4 (100%) | >= 85% | PASS |
| Business Rules Coverage | 5/5 (100%) | >= 85% | PASS |
| Blocked Test Cases | 0 (TC-CHR-043, TC-CHR-049, TC-CHR-063 unblocked by US-CHR-001) | -- | CLEAR |

### Cross-Module Coverage Summary

| Module | User Stories | Test Cases | AC Coverage | Multi-Tenant Tests | Status |
|--------|------------|------------|-------------|-------------------|--------|
| Authentication & Authorization | 10 | 116 | 61/61 (100%) | 23 | PASS |
| Core HR (US-CHR-001, US-CHR-002, US-CHR-003, US-CHR-004, US-CHR-005) | 5 | 170 | 27/27 (100%) | 34 | PASS |
| **TOTAL** | **15** | **286** | **88/88 (100%)** | **57** | |

---

*Note: This traceability matrix covers all test cases for US-CHR-001, US-CHR-002, US-CHR-003, US-CHR-004, and US-CHR-005. All previously blocked test cases (TC-CHR-020, TC-CHR-043, TC-CHR-049, TC-CHR-063) have been unblocked by the delivery of US-CHR-001. BR-5 for US-CHR-002 (HR approval for sensitive field edits) is deferred as it depends on tenant-configurable approval workflows not yet implemented. For US-CHR-003, BR-2 (manager reporting chain scope) is deferred pending Employee.ReportsToEmployeeId and BR-5 search is implemented as ILIKE with tsvector upgrade path documented. The matrix will be extended as additional Core HR user stories (US-CHR-006+) and other modules are authored.*
