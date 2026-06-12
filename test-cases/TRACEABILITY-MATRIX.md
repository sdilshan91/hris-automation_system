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
| FR-1 through BR-6 | (unchanged -- see Auth section above) | | |

### US-AUTH-008, US-AUTH-007, US-AUTH-006, US-AUTH-005 Detailed Requirements Traceability

(Unchanged from previous version -- all Auth detailed traceability tables remain as documented above.)

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
| US-CHR-006 | Organization Tree / Hierarchy Visualization | Should Have | TC-CHR-151, TC-CHR-152, TC-CHR-153, TC-CHR-154, TC-CHR-155, TC-CHR-156, TC-CHR-157, TC-CHR-158, TC-CHR-159, TC-CHR-160, TC-CHR-161, TC-CHR-162, TC-CHR-163, TC-CHR-164, TC-CHR-165, TC-CHR-166, TC-CHR-167, TC-CHR-168, TC-CHR-169, TC-CHR-170, TC-CHR-171 | 21 | 5/5 AC covered |
| Cross-cutting (CHR-001) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-009, TC-CHR-ISO-010, TC-CHR-ISO-011, TC-CHR-ISO-012 | 4 | -- |
| Cross-cutting (CHR-002) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-013, TC-CHR-ISO-014, TC-CHR-ISO-015, TC-CHR-ISO-016 | 4 | -- |
| Cross-cutting (CHR-003) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-017, TC-CHR-ISO-018, TC-CHR-ISO-019, TC-CHR-ISO-020 | 4 | -- |
| Cross-cutting (CHR-004) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003, TC-CHR-ISO-004 | 4 | -- |
| Cross-cutting (CHR-005) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-005, TC-CHR-ISO-006, TC-CHR-ISO-007, TC-CHR-ISO-008 | 4 | -- |
| Cross-cutting (CHR-006) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-021, TC-CHR-ISO-022, TC-CHR-ISO-023, TC-CHR-ISO-024 | 4 | -- |
| **TOTAL** | | | **195 test cases** | **195** | **32/32 AC** |

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
| TC-CHR-012 through TC-CHR-150 | (See previous version -- all unchanged) | | | | |
| TC-CHR-151 | Department hierarchy tree renders with correct parent-child and employee counts | Functional | Critical | US-CHR-006 | AC-1, FR-1, FR-2, FR-5, FR-8, BR-1, BR-2 |
| TC-CHR-152 | Click department node opens detail panel with manager, employees, sub-departments | Functional | Critical | US-CHR-006 | AC-2, FR-1, FR-2, BR-5 |
| TC-CHR-153 | Toggle to reporting structure view shows manager-to-direct-report relationships | Functional | Critical | US-CHR-006 | AC-3, FR-1, FR-2, BR-3 |
| TC-CHR-154 | Search for employee at deepest level -- tree auto-expands, scrolls, highlights | Functional | Critical | US-CHR-006 | AC-4, FR-4, FR-6, BR-1 |
| TC-CHR-155 | Lazy loading -- only top 2 levels load; expanding triggers API call for children | Functional | Critical | US-CHR-006 | AC-5, FR-6, FR-2, NFR-1 |
| TC-CHR-156 | Expand and collapse tree nodes with smooth animation | Functional | High | US-CHR-006 | FR-2 |
| TC-CHR-157 | Pan and zoom interactions on desktop and mobile | Functional | High | US-CHR-006 | FR-3, NFR-2 |
| TC-CHR-158 | Export org chart as PNG contains visible tree structure | Functional | High | US-CHR-006 | FR-7 |
| TC-CHR-159 | Inactive toggle shows inactive departments and employees | Functional | High | US-CHR-006 | BR-4, FR-1, FR-8 |
| TC-CHR-160 | Expand a leaf node with no children -- empty state, no error | Functional | High | US-CHR-006 | FR-2, FR-6, AC-5 |
| TC-CHR-161 | Search with no match returns no highlight and informative empty state | Functional | High | US-CHR-006 | AC-4, FR-4 |
| TC-CHR-162 | Unauthenticated request to org-tree API returns 401 | Security | Critical | US-CHR-006 | FR-8, NFR-3 |
| TC-CHR-163 | Input sanitization -- XSS in org tree search bar | Security | High | US-CHR-006 | FR-4, NFR-3 |
| TC-CHR-164 | Initial top-2-level tree render within 2.5 seconds P95 | Performance | Critical | US-CHR-006 | NFR-1, AC-5 |
| TC-CHR-165 | 200-node tree smooth pan/zoom at approximately 60fps | Performance | Critical | US-CHR-006 | NFR-2, AC-5, FR-3 |
| TC-CHR-166 | WCAG 2.1 AA keyboard arrow-key navigation and screen reader | Accessibility | High | US-CHR-006 | NFR-5, FR-2 |
| TC-CHR-167 | Responsive layout at 360px falls back to accordion/vertical list | Functional | High | US-CHR-006 | NFR-4 |
| TC-CHR-168 | Cross-browser compatibility for org tree (Chrome, Edge, Firefox, Safari) | Functional | Medium | US-CHR-006 | NFR-2, NFR-4 |
| TC-CHR-169 | Tree is read-only -- no drag-and-drop; links to management pages | Functional | High | US-CHR-006 | BR-5, AC-2 |
| TC-CHR-170 | Root departments at top; employees without manager under department in reporting view | Functional | High | US-CHR-006 | BR-2, BR-3, AC-1, AC-3 |
| TC-CHR-171 | Org tree reflects current state -- not historical snapshots | Functional | High | US-CHR-006 | BR-1 |
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
| TC-CHR-ISO-021 | Tenant A org tree shows zero Tenant B departments and employees | Security | Critical | US-CHR-006 | FR-8, NFR-3 |
| TC-CHR-ISO-022 | API rejects org-tree requests without valid tenant context | Security | Critical | US-CHR-006 | FR-8, NFR-3 |
| TC-CHR-ISO-023 | RLS blocks direct DB queries across tenants for org-tree data | Security | Critical | US-CHR-006 | FR-8, NFR-3 |
| TC-CHR-ISO-024 | Cache keys for org-tree data are tenant-scoped | Security | Critical | US-CHR-006 | FR-8, NFR-3 |

### US-CHR-006 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Interactive org chart with department hierarchy, manager avatars/names, employee counts | AC | TC-CHR-151, TC-CHR-170 | Direct |
| AC-2: Click department node shows detail panel with manager, employees, sub-departments, link to management page | AC | TC-CHR-152, TC-CHR-169 | Direct |
| AC-3: Toggle to Reporting Structure view shows manager-to-direct-report relationships | AC | TC-CHR-153, TC-CHR-170 | Direct |
| AC-4: Search for employee, tree highlights and auto-scrolls to matching node, path expanded | AC | TC-CHR-154, TC-CHR-161 | Direct |
| AC-5: Large tree uses lazy loading, smooth 60fps pan/zoom, no browser freeze | AC | TC-CHR-155, TC-CHR-160, TC-CHR-164, TC-CHR-165 | Direct |
| FR-1: Interactive org chart supporting Department Hierarchy and Reporting Structure views | FR | TC-CHR-151, TC-CHR-153, TC-CHR-170 | Direct |
| FR-2: Expand/collapse of tree nodes | FR | TC-CHR-155, TC-CHR-156, TC-CHR-160 | Direct |
| FR-3: Pan and zoom interactions (mouse drag, scroll wheel, pinch-zoom) | FR | TC-CHR-157, TC-CHR-165 | Direct |
| FR-4: Search with auto-scroll and highlight within the tree | FR | TC-CHR-154, TC-CHR-161, TC-CHR-163 | Direct |
| FR-5: Employee count per department node | FR | TC-CHR-151 | Direct |
| FR-6: Lazy-load child nodes for deep hierarchies (API call on expand) | FR | TC-CHR-155, TC-CHR-160 | Direct |
| FR-7: Export org chart as PNG or PDF | FR | TC-CHR-158 | Direct |
| FR-8: All data scoped to current tenant via tenant_id | FR | TC-CHR-151, TC-CHR-162, TC-CHR-ISO-021, TC-CHR-ISO-022, TC-CHR-ISO-023, TC-CHR-ISO-024 | Direct |
| NFR-1: Initial tree render (top 2 levels) within 2.5 seconds (P95) | NFR | TC-CHR-164 | Direct |
| NFR-2: Pan/zoom maintain 60fps on modern browsers | NFR | TC-CHR-165 | Direct |
| NFR-3: All org tree data tenant-isolated via RLS and EF Core global query filters | NFR | TC-CHR-ISO-021, TC-CHR-ISO-022, TC-CHR-ISO-023, TC-CHR-ISO-024 | Direct |
| NFR-4: Responsive: desktop zoomable canvas; mobile (< 768px) collapsible vertical tree | NFR | TC-CHR-167 | Direct |
| NFR-5: WCAG 2.1 AA: keyboard arrow-key navigation, screen reader announces node label and level | NFR | TC-CHR-166 | Direct |
| BR-1: Org tree reflects current state; no historical snapshots | BR | TC-CHR-171 | Direct |
| BR-2: Departments with no parent are root nodes | BR | TC-CHR-151, TC-CHR-170 | Direct |
| BR-3: Employees without manager appear under department node, not under any manager in reporting view | BR | TC-CHR-153, TC-CHR-170 | Direct |
| BR-4: Only active departments/employees shown by default; toggle to show inactive | BR | TC-CHR-159 | Direct |
| BR-5: Tree data is read-only; modifications via management pages | BR | TC-CHR-169 | Direct |

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
| NFR-1 through BR-5 | (unchanged -- see TEST-MATRIX.md for full detail) | | |

### US-CHR-002, US-CHR-001, US-CHR-004, US-CHR-005 Detailed Requirements Traceability

(Unchanged from previous version -- all detailed traceability tables for these stories remain as documented.)

### Coverage Summary (Core HR -- US-CHR-006)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 8/8 (100%) | >= 85% | PASS |
| Non-Functional Requirements Coverage | 5/5 (100%) | >= 85% | PASS |
| Business Rules Coverage | 5/5 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 4 dedicated ISO | >= 3 | PASS |
| Security Test Cases | 6/25 (24%) + 4 ISO = 10/25 (40%) | >= 30% | PASS |
| Performance Test Cases | 2/25 | >= 1 | PASS |
| Accessibility Test Cases | 1/25 | >= 1 | PASS |
| Cross-Browser Test Cases | 2/25 | >= 1 | PASS |
| Blocked Test Cases | 0 | -- | CLEAR |

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
| Core HR (US-CHR-001 through US-CHR-006) | 6 | 195 | 32/32 (100%) | 38 | PASS |
| **TOTAL** | **16** | **311** | **93/93 (100%)** | **61** | |

---

*Note: This traceability matrix covers all test cases for US-CHR-001 through US-CHR-006. All previously blocked test cases (TC-CHR-020, TC-CHR-043, TC-CHR-049, TC-CHR-063) have been unblocked by the delivery of US-CHR-001. BR-5 for US-CHR-002 (HR approval for sensitive field edits) is deferred as it depends on tenant-configurable approval workflows not yet implemented. For US-CHR-003, BR-2 (manager reporting chain scope) is deferred pending Employee.ReportsToEmployeeId and BR-5 search is implemented as ILIKE with tsvector upgrade path documented. US-CHR-006 adds 25 test cases (21 functional/security/performance/accessibility + 4 dedicated ISO) with 100% coverage of all 5 ACs, 8 FRs, 5 NFRs, and 5 BRs. The matrix will be extended as additional Core HR user stories (US-CHR-007+) and other modules are authored.*
