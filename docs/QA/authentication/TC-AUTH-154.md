---
id: TC-AUTH-154
user_story: US-AUTH-011
module: Authentication
priority: high
type: functional
status: draft
created: 2026-07-24
---

# TC-AUTH-154: SSO-originated session is a normal app session — refresh, RBAC, and logout behave identically to local login

## 1. Test Objective
Verify AC-4 / FR-6 / BR-1: because SSO terminates in the existing `JwtService`, an SSO-originated session is indistinguishable downstream from a local-login session (except an `auth_method` marker where present). The refresh flow, RBAC permission claims, and logout/invalidation all work exactly as for a locally-authenticated user — no downstream component special-cases SSO.

## 2. Related Requirements
- User Story: US-AUTH-011
- Acceptance Criteria: AC-4
- Functional Requirements: FR-6
- Business Rules: BR-1

## 3. Preconditions
- Entra SSO configured; acme allow-lists the test directory. `user-a@acme.com` is an acme member with a known role (e.g. Employee) and permission set.
- A successful SSO login for user-a (TC-AUTH-138) established a session (app JWT in memory + refresh cookie).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Refresh endpoint | POST /api/v1/auth/refresh | Uses the httpOnly refresh cookie |
| Logout endpoint | POST /api/v1/auth/logout | |
| A permission-gated endpoint | e.g. GET /api/v1/tenant/leave/requests | Requires user-a's role permissions |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | From the SSO session, `POST /api/v1/auth/refresh`. | HTTP 200 — a fresh app JWT is minted from the SSO-issued refresh token exactly as for local login (US-AUTH-002); rotation behaves normally. |
| 2 | Call a permission-gated endpoint matching user-a's role. | Authorized per the RBAC claims embedded in the SSO-minted JWT — permission evaluation is identical to a local-login JWT (no SSO special-casing). |
| 3 | Call an endpoint user-a's role does NOT permit. | HTTP 403 — RBAC denies exactly as it would for a local session. |
| 4 | `POST /api/v1/auth/logout`, then retry refresh with the old cookie. | Logout invalidates the session; the refresh is rejected (US-AUTH-003) — logout/invalidation is unchanged for SSO sessions. |
| 5 | (If present) Inspect the JWT/audit for the `auth_method` marker (BR-1). | Any `auth_method` claim/flag marks the session as SSO-originated; NO OTHER downstream behavior differs from local login. |

## 6. Postconditions
- The SSO session participated in refresh/RBAC/logout identically to a local session.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
