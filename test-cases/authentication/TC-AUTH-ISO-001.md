---
id: TC-AUTH-ISO-001
user_story: US-AUTH-001, US-AUTH-007
module: Authentication
priority: critical
type: security
status: draft
created: 2026-05-11
---

# TC-AUTH-ISO-001: Tenant A user cannot authenticate as Tenant B

## 1. Test Objective
Verify that a user who has credentials and an active membership in Tenant A cannot use those credentials to authenticate or obtain tokens scoped to Tenant B where they have no membership, ensuring strict multi-tenant authentication isolation.

## 2. Related Requirements
- User Story: US-AUTH-001 (AC-3)
- User Story: US-AUTH-007 (AC-1)
- Functional Requirements: FR-2 (US-AUTH-001), FR-7 (US-AUTH-001)
- Business Rules: BR-1 (US-AUTH-002)

## 3. Preconditions
- Tenant A ("acme") and Tenant B ("globex") are both in `active` state.
- User `john@acme.com` has an active membership ONLY in Tenant A (acme).
- User `john@acme.com` does NOT have any membership in Tenant B (globex).
- User `jane@globex.com` has an active membership ONLY in Tenant B (globex).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme (acme.yourhrm.com) | User john has membership |
| Tenant B | globex (globex.yourhrm.com) | User john has NO membership |
| User A | john@acme.com | Acme-only user |
| User A Password | S3cure!Pass2026 | Valid password |
| User B | jane@globex.com | Globex-only user |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/auth/login` to `acme.yourhrm.com` with john's credentials | HTTP 200; login succeeds in Tenant A. JWT has `tenant_id` = acme UUID. |
| 2 | Send `POST /api/v1/auth/login` to `globex.yourhrm.com` with john's credentials | HTTP 403 Forbidden with "No active membership for this organization." |
| 3 | Verify no JWT is issued with `tenant_id` = globex UUID | No access token in response. |
| 4 | Verify no refresh token is created for john in globex | No `refresh_token` record for john + globex tenant pair. |
| 5 | Attempt to use john's acme JWT to access globex API endpoints by manually changing the Host header | Request is rejected; tenant resolution middleware validates tenant context against the JWT's `tenant_id` claim. |
| 6 | Attempt to forge a JWT with globex `tenant_id` but signed with a valid key | Signature validation fails or tenant context mismatch is detected. |
| 7 | Send `POST /api/v1/auth/refresh` to `globex.yourhrm.com` with john's acme refresh token cookie | HTTP 401; refresh token is bound to acme tenant, not globex. |
| 8 | Verify jane (globex user) cannot authenticate at `acme.yourhrm.com` | HTTP 403; same isolation in reverse direction. |
| 9 | Verify that cross-tenant credential sharing does not expose any data from either tenant | No data leakage in error responses. |

## 6. Postconditions
- Authentication is strictly tenant-scoped.
- No cross-tenant tokens can be issued or reused.
- Error responses do not leak data about the other tenant.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
