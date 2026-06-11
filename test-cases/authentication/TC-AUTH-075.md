---
id: TC-AUTH-075
user_story: US-AUTH-009
module: Authentication
priority: critical
type: security
status: draft
created: 2026-06-11
---

# TC-AUTH-075: Cross-tenant session isolation -- policies and sessions are tenant-scoped

## 1. Test Objective
Verify that (a) tenant A's session policies (idle timeout, absolute timeout, concurrent session limits, strategy) do not affect tenant B's sessions, (b) a tenant A admin cannot view or revoke sessions belonging to tenant B users, (c) session counts are per tenant-user pair (not global), and (d) RLS prevents direct cross-tenant session data access.

## 2. Related Requirements
- User Story: US-AUTH-009
- Acceptance Criteria: AC-1, AC-4, AC-5
- Functional Requirements: FR-1, FR-5, FR-6, FR-8
- Non-Functional Requirements: NFR-5
- Business Rules: BR-1

## 3. Preconditions
- Tenant A ("acme"): `maxConcurrentSessions = 2`, `idleTimeoutMinutes = 15`, `absoluteTimeoutHours = 8`.
- Tenant B ("globex"): `maxConcurrentSessions = 10`, `idleTimeoutMinutes = 60`, `absoluteTimeoutHours = 24`.
- User `multi@yourhrm.test` has memberships in both tenants.
- User `adminA@acme.com` is admin in tenant A.
- User `userB@globex.com` is an employee in tenant B only, with 2 active sessions.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | maxConcurrentSessions = 2 |
| Tenant B | globex | maxConcurrentSessions = 10 |
| Multi-tenant user | multi@yourhrm.test | Member of both |
| Tenant A admin | adminA@acme.com | Admin in acme only |
| Tenant B user | userB@globex.com | Employee in globex only |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | **Policy isolation:** `multi@yourhrm.test` logs in to tenant A from 2 devices (reaching A's limit of 2). Attempt 3rd login in tenant A (deny_new). | HTTP 409; tenant A limit is enforced. |
| 2 | `multi@yourhrm.test` logs in to tenant B from a new device. | HTTP 200; login succeeds. Tenant B's limit is 10, so tenant A's limit does not affect tenant B. |
| 3 | Verify the multi-tenant user has 2 active sessions in tenant A and 1 in tenant B. | Session counts are per tenant-user pair. |
| 4 | **Idle timeout isolation:** In tenant A (idleTimeout=15min), wait 20 minutes without activity. Attempt refresh in tenant A. | HTTP 401; idle expired per tenant A's policy. |
| 5 | The same user's tenant B session has been idle the same 20 minutes. Attempt refresh in tenant B. | HTTP 200; tenant B's idle timeout is 60 minutes, so the session is still valid. |
| 6 | **Admin view isolation:** As `adminA@acme.com`, call `GET /api/v1/tenant/users/{userB-id}/sessions` (attempting to view tenant B user's sessions via tenant A context). | HTTP 404 or HTTP 403; the user does not exist in tenant A's context, or access is denied. |
| 7 | As `adminA@acme.com`, call `POST /api/v1/tenant/users/{userB-id}/sessions/revoke` with a known tenant B session ID. | HTTP 404 or HTTP 403; cannot revoke cross-tenant sessions. |
| 8 | Verify `userB@globex.com`'s sessions are unaffected. | Both sessions in tenant B remain active. |
| 9 | **RLS verification:** If possible, execute a direct DB query (simulating SQL injection or bypassed application layer): `SELECT * FROM refresh_tokens WHERE tenant_id = '{tenantB-id}'` from a tenant A connection context. | RLS policy blocks the query or returns 0 rows. |
| 10 | **Session list scoping:** As `multi@yourhrm.test` in tenant A, call `GET /api/v1/auth/me/sessions`. | Only tenant A sessions are returned; tenant B sessions are not visible. |
| 11 | Switch to tenant B context and call `GET /api/v1/auth/me/sessions`. | Only tenant B sessions are returned; tenant A sessions are not visible. |

## 6. Postconditions
- Each tenant's session policies are applied independently.
- No cross-tenant session data leakage occurred.
- Session counts are correctly scoped to tenant-user pairs.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
