---
id: TC-AUTH-060
user_story: US-AUTH-008
module: Authentication
priority: critical
type: security
status: draft
created: 2026-06-09
---

# TC-AUTH-060: Unauthorized, suspended, and terminated tenant switch attempts are rejected

## 1. Test Objective
Verify that tenant switching rejects unauthorized membership, inactive membership, suspended tenants, and terminated tenants without issuing target tokens.

## 2. Related Requirements
- User Story: US-AUTH-008
- Acceptance Criteria: AC-3, AC-4
- Functional Requirements: FR-5, FR-6
- Business Rules: BR-5

## 3. Preconditions
- User `single.user@yourhrm.test` is authenticated in tenant A (`acme`).
- Tenant `secretcorp` exists, but the user has no membership in it.
- Tenant `suspcorp` exists with status `suspended`.
- Tenant `termcorp` exists with status `terminated`.
- User has inactive, suspended, or terminated membership fixtures where needed.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Current tenant | acme | Active source tenant |
| Unauthorized tenant | secretcorp | No user_tenant row |
| Suspended tenant | suspcorp | Tenant status `suspended` |
| Terminated tenant | termcorp | Tenant status `terminated` |
| Expected membership error | You do not have an active membership in this organization. | 403 body |
| Expected tenant-state error | Target organization is currently unavailable. | 403 body |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `POST /api/v1/auth/switch-tenant` with `secretcorp` tenant ID. | HTTP 403 with active-membership error; no access token in body. |
| 2 | Inspect response headers for step 1. | No target-tenant refresh cookie is set. |
| 3 | Call switch endpoint with a valid tenant ID where the user membership status is not `active`. | HTTP 403 with active-membership error; no access or refresh token is issued. |
| 4 | Call switch endpoint with `suspcorp` tenant ID where user has membership. | HTTP 403 with target-unavailable error; no token is issued. |
| 5 | Call switch endpoint with `termcorp` tenant ID where user has membership. | HTTP 403 with target-unavailable error; no token is issued. |
| 6 | Open tenant switcher UI for the authenticated user. | Suspended and terminated tenants are listed if membership exists, grayed out, and marked with the correct status badge. |
| 7 | Attempt to activate a disabled suspended or terminated tenant row. | UI prevents switch or shows the same unavailable message without issuing a request that can create tokens. |
| 8 | Verify the source tenant session after each failed attempt. | Original tenant A access and refresh tokens remain usable. |
| 9 | Review security/audit events. | Denials are logged with user ID, attempted tenant ID, reason, IP, and user agent. |

## 6. Postconditions
- No unauthorized, suspended, or terminated target session exists.
- Source session remains valid.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
