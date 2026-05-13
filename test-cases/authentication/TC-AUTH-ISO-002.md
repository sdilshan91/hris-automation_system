---
id: TC-AUTH-ISO-002
user_story: US-AUTH-002, US-AUTH-006
module: Authentication
priority: critical
type: security
status: draft
created: 2026-05-11
---

# TC-AUTH-ISO-002: JWT claims include correct tenant_id

## 1. Test Objective
Verify that every JWT access token issued by the system contains the correct `tenant_id` claim matching the resolved tenant from the subdomain, and that roles/permissions are exclusively for that tenant with no cross-tenant claim contamination.

## 2. Related Requirements
- User Story: US-AUTH-002 (FR-1)
- User Story: US-AUTH-006 (AC-7, BR-1)
- User Story: US-AUTH-008 (AC-5)

## 3. Preconditions
- User `multi@acme.com` has active memberships in:
  - Tenant A ("acme"): roles = ["Tenant Admin"], permissions = admin-level
  - Tenant B ("globex"): roles = ["Employee"], permissions = employee-level
- Both tenants are in `active` state.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | multi@acme.com | Multi-tenant user |
| Tenant A ID | (acme UUID) | From tenant table |
| Tenant B ID | (globex UUID) | From tenant table |
| Tenant A roles | ["Tenant Admin"] | Admin in acme |
| Tenant B roles | ["Employee"] | Employee in globex |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate at `acme.yourhrm.com` | JWT issued successfully. |
| 2 | Decode the JWT and verify `tenant_id` claim | Exactly matches acme's UUID. Does NOT match globex UUID. |
| 3 | Verify `user_tenant_id` claim | Matches the `user_tenant` record for multi@acme.com + acme. |
| 4 | Verify `roles` claim | Contains ONLY `["Tenant Admin"]` -- acme roles. |
| 5 | Verify `permissions` claim | Contains ONLY acme Tenant Admin permissions. No employee-level-only permissions from globex. |
| 6 | Verify no globex-specific data in any JWT claim | No globex UUID, roles, or permissions anywhere in the token. |
| 7 | Authenticate at `globex.yourhrm.com` | JWT issued successfully. |
| 8 | Decode the JWT and verify `tenant_id` claim | Exactly matches globex's UUID. Does NOT match acme UUID. |
| 9 | Verify `roles` claim | Contains ONLY `["Employee"]` -- globex roles. |
| 10 | Verify `permissions` claim | Contains ONLY globex Employee permissions. No admin permissions from acme. |
| 11 | After tenant switch (US-AUTH-008): switch from acme to globex | New JWT has `tenant_id` = globex UUID, `roles` = ["Employee"]. Zero acme claims. |
| 12 | Verify `is_impersonation` claim is `false` for regular logins | Claim correctly reflects non-impersonation state. |

## 6. Postconditions
- Each JWT contains exclusively the claims for the authenticated tenant.
- No cross-tenant role or permission leakage exists.
- JWT structure is verifiable and consistent across tenants.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
