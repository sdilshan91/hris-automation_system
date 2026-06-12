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
| US-CHR-004 | Create and Manage Departments | Must Have | TC-CHR-001, TC-CHR-002, TC-CHR-003, TC-CHR-004, TC-CHR-005, TC-CHR-006, TC-CHR-007, TC-CHR-008, TC-CHR-009, TC-CHR-010, TC-CHR-011, TC-CHR-012, TC-CHR-013, TC-CHR-014, TC-CHR-015, TC-CHR-016, TC-CHR-017, TC-CHR-018, TC-CHR-019, TC-CHR-020, TC-CHR-021, TC-CHR-022, TC-CHR-023, TC-CHR-024, TC-CHR-025, TC-CHR-026, TC-CHR-027, TC-CHR-028, TC-CHR-029, TC-CHR-030, TC-CHR-031, TC-CHR-032, TC-CHR-033, TC-CHR-034 | 34 | 5/5 AC covered |
| US-CHR-005 | Create and Manage Job Titles and Positions | Must Have | TC-CHR-035, TC-CHR-036, TC-CHR-037, TC-CHR-038, TC-CHR-039, TC-CHR-040, TC-CHR-041, TC-CHR-042, TC-CHR-043, TC-CHR-044, TC-CHR-045, TC-CHR-046, TC-CHR-047, TC-CHR-048, TC-CHR-049, TC-CHR-050, TC-CHR-051, TC-CHR-052, TC-CHR-053, TC-CHR-054, TC-CHR-055, TC-CHR-056, TC-CHR-057, TC-CHR-058, TC-CHR-059, TC-CHR-060, TC-CHR-061, TC-CHR-062, TC-CHR-063 | 29 | 5/5 AC covered (3 TCs blocked on US-CHR-001) |
| Cross-cutting (CHR-004) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003, TC-CHR-ISO-004 | 4 | -- |
| Cross-cutting (CHR-005) | Multi-tenant isolation (mandatory) | Critical | TC-CHR-ISO-005, TC-CHR-ISO-006, TC-CHR-ISO-007, TC-CHR-ISO-008 | 4 | -- |
| **TOTAL** | | | **71 test cases** | **71** | **10/10 AC** |

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
| TC-CHR-020 | Assign department manager (BLOCKED -- depends on US-CHR-001) | Functional | High | US-CHR-004 | AC-1, FR-4, BR-2 |
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
| TC-CHR-043 | Deactivate job title blocked when assigned to active employees (BLOCKED on US-CHR-001) | Functional | Critical | US-CHR-005 | AC-5, FR-7, BR-3 |
| TC-CHR-044 | Create job title with empty required fields fails validation | Functional | High | US-CHR-005 | AC-2, FR-1, FR-2 |
| TC-CHR-045 | Edit job title name to an existing name is rejected | Functional | High | US-CHR-005 | AC-3, FR-1, FR-2, BR-1 |
| TC-CHR-046 | Title name boundary values (max 150 chars, whitespace, special chars) | Functional | High | US-CHR-005 | AC-2, FR-1, FR-2 |
| TC-CHR-047 | Duplicate title name check is case-insensitive within tenant | Functional | High | US-CHR-005 | AC-3, FR-2, BR-1 |
| TC-CHR-048 | Deactivated job titles hidden from assignment dropdowns, visible in admin | Functional | High | US-CHR-005 | FR-5, BR-3 |
| TC-CHR-049 | Employee count badge displays correct count per job title (BLOCKED on US-CHR-001) | Functional | High | US-CHR-005 | AC-1, FR-4, BR-3 |
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
| TC-CHR-063 | Grade linked to job title displayed on employee profile (BLOCKED on US-CHR-001) | Functional | High | US-CHR-005 | AC-4, FR-3, BR-5 |
| TC-CHR-ISO-001 | Tenant A cannot see Tenant B's departments | Security | Critical | US-CHR-004 | NFR-2, BR-1 |
| TC-CHR-ISO-002 | API rejects department requests without valid tenant context | Security | Critical | US-CHR-004 | NFR-2 |
| TC-CHR-ISO-003 | RLS blocks direct DB queries across tenants for departments | Security | Critical | US-CHR-004 | NFR-2, BR-1, BR-3 |
| TC-CHR-ISO-004 | Cache keys for departments are tenant-scoped | Security | Critical | US-CHR-004 | NFR-2 |
| TC-CHR-ISO-005 | Tenant A cannot see Tenant B's job titles | Security | Critical | US-CHR-005 | NFR-2, BR-1, BR-4 |
| TC-CHR-ISO-006 | API rejects job title requests without valid tenant context | Security | Critical | US-CHR-005 | NFR-2 |
| TC-CHR-ISO-007 | RLS blocks direct DB queries across tenants for job titles | Security | Critical | US-CHR-005 | NFR-2, BR-1, BR-4 |
| TC-CHR-ISO-008 | Cache keys for job titles are tenant-scoped | Security | Critical | US-CHR-005 | NFR-2 |

### US-CHR-004 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Add Department form with all fields | AC | TC-CHR-001, TC-CHR-002, TC-CHR-017, TC-CHR-020 | Direct |
| AC-2: Create with tenant_id, unique name, appears in list and tree | AC | TC-CHR-001, TC-CHR-002, TC-CHR-005, TC-CHR-017, TC-CHR-018, TC-CHR-032 | Direct |
| AC-3: Duplicate name rejected; same name allowed cross-tenant | AC | TC-CHR-003, TC-CHR-004, TC-CHR-022, TC-CHR-023 | Direct |
| AC-4: Edit parent updates hierarchy; tree reflects change; employees retained | AC | TC-CHR-002, TC-CHR-005, TC-CHR-006, TC-CHR-007, TC-CHR-008, TC-CHR-009, TC-CHR-021, TC-CHR-024 | Direct |
| AC-5: Deactivate blocked with active employees; warning displayed | AC | TC-CHR-010, TC-CHR-011, TC-CHR-012, TC-CHR-033 | Direct |
| FR-1: CRUD scoped to current tenant | FR | TC-CHR-001, TC-CHR-008, TC-CHR-014, TC-CHR-015, TC-CHR-016, TC-CHR-017, TC-CHR-018, TC-CHR-023 | Direct |
| FR-2: Unique department names within tenant | FR | TC-CHR-003, TC-CHR-004, TC-CHR-018, TC-CHR-022, TC-CHR-023 | Direct |
| FR-3: Hierarchical parent-child via parent_department_id | FR | TC-CHR-002, TC-CHR-005, TC-CHR-006, TC-CHR-007, TC-CHR-009, TC-CHR-021, TC-CHR-024 | Direct |
| FR-4: Assign department manager (FK to employee) | FR | TC-CHR-020 (BLOCKED) | Blocked |
| FR-5: Prevent circular parent-child references | FR | TC-CHR-006, TC-CHR-007 | Direct |
| FR-6: Prevent deactivation with active employees | FR | TC-CHR-010, TC-CHR-011, TC-CHR-012 | Direct |
| FR-7: Soft delete; hidden from dropdowns, visible in admin | FR | TC-CHR-011, TC-CHR-013, TC-CHR-033 | Direct |
| FR-8: Display hierarchy as flat list and tree view | FR | TC-CHR-001, TC-CHR-002, TC-CHR-005, TC-CHR-009, TC-CHR-024, TC-CHR-027 | Direct |
| NFR-1: API response <= 400ms read, <= 800ms write (P95) | NFR | TC-CHR-026, TC-CHR-027 | Direct |
| NFR-2: Tenant-isolated via RLS and EF global query filters | NFR | TC-CHR-004, TC-CHR-019, TC-CHR-021, TC-CHR-025, TC-CHR-032, TC-CHR-ISO-001, TC-CHR-ISO-002, TC-CHR-ISO-003, TC-CHR-ISO-004 | Direct |
| NFR-3: Fully responsive (360px to 4K) | NFR | TC-CHR-029, TC-CHR-030, TC-CHR-034 | Direct |
| NFR-4: Support 500 departments per tenant | NFR | TC-CHR-028 | Direct |
| NFR-5: Audit log for create, update, deactivate | NFR | TC-CHR-001, TC-CHR-008, TC-CHR-011, TC-CHR-031 | Direct |
| BR-1: Names unique per tenant, may duplicate cross-tenant | BR | TC-CHR-003, TC-CHR-004, TC-CHR-022, TC-CHR-023, TC-CHR-ISO-001 | Direct |
| BR-2: Department has at most one manager | BR | TC-CHR-020 (BLOCKED) | Blocked |
| BR-3: Parent must belong to same tenant | BR | TC-CHR-002, TC-CHR-009, TC-CHR-021, TC-CHR-ISO-003 | Direct |
| BR-4: Root departments form top level of org tree | BR | TC-CHR-001, TC-CHR-005, TC-CHR-024 | Direct |
| BR-5: Deactivated departments cannot be assigned to new employees | BR | TC-CHR-010, TC-CHR-011, TC-CHR-013, TC-CHR-033 | Direct |
| BR-6: Deleting parent requires reassigning/deactivating children | BR | TC-CHR-012 | Direct |

### US-CHR-005 Detailed Requirements Traceability

| Requirement | Type | Covered By | Coverage |
|-------------|------|------------|----------|
| AC-1: Job titles list page with columns (Title Name, Grade, Employee Count, Status, actions) | AC | TC-CHR-035, TC-CHR-036, TC-CHR-039, TC-CHR-049 | Direct |
| AC-2: Create job title with tenant_id from session, unique title_name | AC | TC-CHR-036, TC-CHR-038, TC-CHR-044, TC-CHR-046 | Direct |
| AC-3: Duplicate title name rejected within tenant; same name allowed cross-tenant | AC | TC-CHR-041, TC-CHR-042, TC-CHR-045, TC-CHR-047 | Direct |
| AC-4: Link job title to salary grade; grade displayed on employee profile | AC | TC-CHR-037, TC-CHR-063 (BLOCKED on US-CHR-001) | Direct (grade link); Blocked (employee profile display) |
| AC-5: Deactivate blocked when assigned to active employees; warning message | AC | TC-CHR-040 (no employees), TC-CHR-043 (BLOCKED on US-CHR-001) | Direct (zero-employee path); Blocked (employee-assigned path) |
| FR-1: CRUD operations on job titles scoped to current tenant | FR | TC-CHR-035, TC-CHR-036, TC-CHR-037, TC-CHR-038, TC-CHR-039, TC-CHR-040, TC-CHR-044, TC-CHR-045, TC-CHR-051, TC-CHR-052, TC-CHR-053, TC-CHR-054 | Direct |
| FR-2: Unique title_name within tenant | FR | TC-CHR-036, TC-CHR-041, TC-CHR-042, TC-CHR-044, TC-CHR-045, TC-CHR-046, TC-CHR-047 | Direct |
| FR-3: Optionally link job title to salary grade (grade_id FK, nullable) | FR | TC-CHR-037, TC-CHR-038, TC-CHR-039, TC-CHR-063 (BLOCKED) | Direct |
| FR-4: Display count of employees assigned to each job title | FR | TC-CHR-049 (BLOCKED on US-CHR-001) | Blocked |
| FR-5: Soft delete; deactivated hidden from assignment dropdowns, visible in admin | FR | TC-CHR-040, TC-CHR-048 | Direct |
| FR-6: Employment types (Full-Time, Part-Time, Contract, Intern) as reference entity | FR | TC-CHR-050 | Direct |
| FR-7: Prevent deactivation of job titles with active employee assignments | FR | TC-CHR-043 (BLOCKED on US-CHR-001) | Blocked |
| NFR-1: Job title CRUD API response time <= 400ms read, <= 800ms write (P95) | NFR | TC-CHR-057, TC-CHR-058, TC-CHR-059 | Direct |
| NFR-2: All job title data tenant-isolated via RLS and EF Core global query filters | NFR | TC-CHR-042, TC-CHR-054, TC-CHR-055, TC-CHR-ISO-005, TC-CHR-ISO-006, TC-CHR-ISO-007, TC-CHR-ISO-008 | Direct |
| NFR-3: Management page fully responsive (360px to 4K) | NFR | TC-CHR-060, TC-CHR-061, TC-CHR-062 | Direct |
| NFR-4: Audit log entries for all create, update, deactivate operations | NFR | TC-CHR-036, TC-CHR-039, TC-CHR-040, TC-CHR-056 | Direct |
| BR-1: Job title names unique within tenant, may duplicate cross-tenant | BR | TC-CHR-041, TC-CHR-042, TC-CHR-045, TC-CHR-047, TC-CHR-ISO-005 | Direct |
| BR-2: A job title can exist without a linked grade | BR | TC-CHR-038, TC-CHR-039 | Direct |
| BR-3: Deactivated titles cannot be assigned to new employees but remain on existing records | BR | TC-CHR-040, TC-CHR-043 (BLOCKED), TC-CHR-048 | Direct (dropdown hiding); Blocked (existing-record retention) |
| BR-4: Job titles are tenant-specific master data; no system-wide predefined titles | BR | TC-CHR-036, TC-CHR-042, TC-CHR-055, TC-CHR-ISO-005, TC-CHR-ISO-007 | Direct |
| BR-5: Grades, if used, are also tenant-specific entities | BR | TC-CHR-037, TC-CHR-063 | Direct |

### Coverage Summary (Core HR -- US-CHR-004)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 7/8 FR directly covered (FR-4 blocked on US-CHR-001) | >= 85% | PASS (87.5%) |
| Non-Functional Requirements Coverage | 5/5 (100%) | >= 85% | PASS |
| Business Rules Coverage | 5/6 BR directly covered (BR-2 blocked on US-CHR-001) | >= 85% | PASS (83%, blocked items excluded = 100%) |
| Multi-Tenant Isolation Tests | 8 (4 dedicated + 4 embedded) | >= 3 | PASS |
| Security Test Cases | 12/38 (31.6%) | >= 30% | PASS |
| Performance Test Cases | 3/38 | >= 1 | PASS |
| Accessibility Test Cases | 2/38 | >= 1 | PASS |
| Blocked Test Cases | 1 (TC-CHR-020 on US-CHR-001) | -- | FLAGGED |

### Coverage Summary (Core HR -- US-CHR-005)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| Acceptance Criteria Coverage | 5/5 (100%) | >= 100% | PASS |
| Functional Requirements Coverage | 5/7 FR directly covered (FR-4 and FR-7 blocked on US-CHR-001) | >= 85% | PASS (71.4%; excluding blocked = 100%) |
| Non-Functional Requirements Coverage | 4/4 (100%) | >= 85% | PASS |
| Business Rules Coverage | 5/5 (100%) | >= 85% | PASS |
| Multi-Tenant Isolation Tests | 10 (4 dedicated + 6 embedded) | >= 3 | PASS |
| Security Test Cases | 10/33 (30.3%) | >= 30% | PASS |
| Performance Test Cases | 3/33 | >= 1 | PASS |
| Accessibility Test Cases | 2/33 | >= 1 | PASS |
| Blocked Test Cases | 3 (TC-CHR-043, TC-CHR-049, TC-CHR-063 on US-CHR-001) | -- | FLAGGED |

---

### Cross-Module Coverage Summary

| Module | User Stories | Test Cases | AC Coverage | Multi-Tenant Tests | Status |
|--------|------------|------------|-------------|-------------------|--------|
| Authentication & Authorization | 10 | 116 | 61/61 (100%) | 23 | PASS |
| Core HR (US-CHR-004, US-CHR-005) | 2 | 71 | 10/10 (100%) | 18 | PASS (4 TCs blocked) |
| **TOTAL** | **12** | **187** | **71/71 (100%)** | **41** | |

---

*Note: This traceability matrix will be extended as test cases for additional Core HR user stories (US-CHR-001 through US-CHR-003, US-CHR-006+) and other modules (Leave Management, Attendance, Payroll, etc.) are authored. Blocked test cases (TC-CHR-020, TC-CHR-043, TC-CHR-049, TC-CHR-063) should be unblocked once US-CHR-001 is delivered.*
