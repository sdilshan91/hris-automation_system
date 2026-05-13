---
id: TC-AUTH-023
user_story: US-AUTH-008
module: Authentication
priority: critical
type: security
status: draft
created: 2026-05-11
---

# TC-AUTH-023: User cannot switch to tenant they don't belong to

## 1. Test Objective
Verify that a user who attempts to switch to a tenant where they have no active membership receives a 403 Forbidden response and no tokens are issued for the unauthorized tenant.

## 2. Related Requirements
- User Story: US-AUTH-008
- Acceptance Criteria: AC-3, AC-4
- Functional Requirements: FR-5, FR-6

## 3. Preconditions
- User `john@acme.com` is authenticated in tenant "acme".
- The user does NOT have a membership in tenant "secretcorp".
- Tenant "secretcorp" exists and is in `active` state.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | john@acme.com | Single-tenant user |
| Current Tenant | acme | Active membership |
| Target Tenant (no membership) | secretcorp | No user_tenant record |
| Target Tenant (suspended) | suspcorp | Has membership but tenant suspended |
| Switch endpoint | POST /api/v1/auth/switch-tenant | Accept { tenantId } |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/auth/switch-tenant` with `{ tenantId: "<secretcorp-tenant-id>" }` | HTTP 403 Forbidden with "You do not have an active membership in this organization." |
| 2 | Verify no JWT is issued for secretcorp | No access token in response body. |
| 3 | Verify no refresh token cookie is set for secretcorp | No `Set-Cookie` header for new refresh token. |
| 4 | Verify the current acme session remains unaffected | Acme JWT and refresh token are still valid. |
| 5 | Send `POST /api/v1/auth/switch-tenant` with a completely fabricated tenant UUID | HTTP 403 Forbidden or 404 Not Found; no tokens issued. |
| 6 | If user has a membership in a suspended tenant "suspcorp": send switch request to suspcorp | HTTP 403 Forbidden with "The target organization is currently unavailable." |
| 7 | Verify the tenant switcher UI shows suspended tenants as grayed out with "Suspended" badge | UI prevents clicking or shows disabled state for suspended tenants. |
| 8 | Verify unauthorized switch attempts are logged as security events | Audit record with user_id, attempted tenant_id, reason for denial. |

## 6. Postconditions
- No tokens are issued for unauthorized or inaccessible tenants.
- The user's current session remains valid and unaffected.
- Security events are logged for unauthorized switch attempts.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
