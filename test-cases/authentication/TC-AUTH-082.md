---
id: TC-AUTH-082
user_story: US-AUTH-009
module: Authentication
priority: high
type: security
status: draft
created: 2026-06-11
---

# TC-AUTH-082: System admin sessions follow system policy; impersonation sessions excluded from count

## 1. Test Objective
Verify that (a) system admin sessions on `admin.yourhrm.com` follow the system-level session policy rather than any individual tenant's policy (BR-2), and (b) impersonation sessions do not count toward the impersonated user's concurrent session limit (BR-3).

## 2. Related Requirements
- User Story: US-AUTH-009
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-5
- Business Rules: BR-2, BR-3

## 3. Preconditions
- System-level session policy: `maxConcurrentSessions = 10`, `idleTimeoutMinutes = 120`, `absoluteTimeoutHours = 24`.
- Tenant "acme" session policy: `maxConcurrentSessions = 2`, `idleTimeoutMinutes = 15`.
- System admin user `sysadmin@yourhrm.com` has access to `admin.yourhrm.com`.
- Tenant admin `admin@acme.com` can impersonate `employee@acme.com` (if impersonation is supported).
- `employee@acme.com` has `maxConcurrentSessions = 2` and already has 2 active sessions.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| System admin | sysadmin@yourhrm.com | System Super Admin |
| System subdomain | admin.yourhrm.com | System context |
| System idle timeout | 120 minutes | System-level policy |
| Tenant A idle timeout | 15 minutes | Tenant-level policy |
| Tenant A max sessions | 2 | Per-tenant limit |
| Impersonating admin | admin@acme.com | Tenant Admin |
| Impersonated user | employee@acme.com | Employee, 2 active sessions |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | **System policy:** Log in as `sysadmin@yourhrm.com` on `admin.yourhrm.com`. | Session is created under system context. |
| 2 | Wait 20 minutes without activity (exceeds tenant A's 15-min idle timeout but within system's 120-min). | Session is idle for 20 minutes. |
| 3 | Call `POST /api/v1/auth/refresh` from the system admin session. | HTTP 200; refresh succeeds. System-level idle timeout of 120 minutes applies, not tenant A's 15 minutes. |
| 4 | Wait 125 minutes without activity (exceeds system's 120-min idle timeout). | Session is idle for 125 minutes. |
| 5 | Call `POST /api/v1/auth/refresh`. | HTTP 401; system idle timeout is enforced. |
| 6 | **Impersonation exclusion:** Verify `employee@acme.com` has exactly 2 active sessions (at the limit). | 2 active refresh tokens for this user-tenant pair. |
| 7 | As `admin@acme.com`, initiate an impersonation session for `employee@acme.com`. | Impersonation session is created (if the feature is supported). |
| 8 | Verify that the impersonation session does NOT count toward `employee@acme.com`'s concurrent session limit. | Active session count for `employee@acme.com` (excluding impersonation) remains 2, not 3. |
| 9 | `employee@acme.com` attempts to log in from a new device (3rd regular session with max=2). | HTTP 409 (deny_new) or oldest revoked (revoke_oldest). The impersonation session is not considered in the count, so only the 2 real sessions count. |
| 10 | End the impersonation session. | Impersonation session is terminated cleanly. |
| 11 | Verify `employee@acme.com`'s regular sessions are unaffected by the impersonation session lifecycle. | The 2 regular sessions remain active (or handled per step 9's strategy). |

## 6. Postconditions
- System admin sessions are governed by system-level policies.
- Impersonation sessions are excluded from the impersonated user's concurrent session count.
- Regular users' session limits are not inflated by impersonation activity.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
