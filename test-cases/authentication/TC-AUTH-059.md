---
id: TC-AUTH-059
user_story: US-AUTH-008
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-09
---

# TC-AUTH-059: My-tenants list and switch to tenant B

## 1. Test Objective
Verify that a cross-tenant user can retrieve all memberships from `my-tenants`, select tenant B, and receive a target-tenant session without re-entering credentials.

## 2. Related Requirements
- User Story: US-AUTH-008
- Acceptance Criteria: AC-1, AC-2
- Functional Requirements: FR-1, FR-2, FR-3, FR-5, FR-6, FR-8, FR-9
- Business Rules: BR-1, BR-5

## 3. Preconditions
- User `multi.user@yourhrm.test` is authenticated in tenant A (`acme`) with valid access and refresh tokens.
- The same user has active memberships in tenant A and tenant B (`globex`).
- Tenant A and tenant B are in `active` or `trial` state.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Current tenant | acme | Tenant A |
| Target tenant | globex | Tenant B |
| Tenant A role | Manager | Source role |
| Tenant B role | Employee | Target role |
| List endpoint | GET /api/v1/auth/my-tenants | Authenticated request |
| Switch endpoint | POST /api/v1/auth/switch-tenant | Body: `{ "tenantId": "<globex-id>" }` |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/auth/my-tenants` using tenant A JWT. | HTTP 200 is returned. |
| 2 | Inspect each tenant item in the response. | Each item includes `tenantId`, `subdomain`, `name`, `logoUrl`, `status`, `roles`, and `isCurrentTenant`. |
| 3 | Verify tenant A entry. | Tenant A has `isCurrentTenant: true` and source roles only. |
| 4 | Verify tenant B entry. | Tenant B is present with `isCurrentTenant: false`, status `active` or `trial`, and tenant B roles only. |
| 5 | Call `POST /api/v1/auth/switch-tenant` with tenant B ID. | HTTP 200 is returned with `accessToken`, target tenant summary, and redirect URL. |
| 6 | Inspect response tenant and redirect fields. | `tenant.tenantId` equals tenant B, `tenant.subdomain` is `globex`, and `redirectUrl` is `https://globex.yourhrm.com/dashboard`. |
| 7 | Verify refresh-token handling. | A new HTTP-only secure refresh token scoped to tenant B is set. |
| 8 | Navigate browser to the redirect URL. | Dashboard loads under tenant B context without credential prompt. |
| 9 | Call `GET /api/v1/auth/me` using tenant B JWT. | Profile contains current tenant B context and all memberships. |

## 6. Postconditions
- User has an active tenant B access token and refresh token.
- Tenant A session is not invalidated by the switch.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
