---
id: TC-AUTH-037
user_story: US-AUTH-005
module: Authentication
priority: high
type: security
status: draft
created: 2026-06-03
---

# TC-AUTH-037: Cross-tenant MFA enforcement

## 1. Test Objective
Verify the cross-tenant MFA behavior for a user who belongs to multiple tenants with differing MFA policies. The TOTP secret is global to the user, but enforcement is per-tenant. If any tenant requires MFA for the user's role, the user cannot disable MFA globally.

## 2. Related Requirements
- User Story: US-AUTH-005
- Acceptance Criteria: AC-1, AC-6
- Functional Requirements: FR-6, FR-7, FR-9
- Business Rules: BR-2, BR-3

## 3. Preconditions
- User `multi@example.com` has memberships in two tenants:
  - **TenantA ("alpha"):** `mfaPolicy = "off"`, user has "Employee" role.
  - **TenantB ("beta"):** `mfaPolicy = "required"`, `mfaRequiredRoles = ["Employee"]`, user has "Employee" role.
- User has MFA enabled globally (`mfa_enabled = true`) with a valid TOTP secret (required by TenantB).
- User has valid password credentials.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | multi@example.com | Member of both TenantA and TenantB |
| Password | S3cure!Pass2026 | Correct password |
| TenantA | alpha | mfaPolicy=off |
| TenantB | beta | mfaPolicy=required, mfaRequiredRoles=["Employee"] |
| Login endpoint | POST /api/v1/auth/login | With tenant context |
| Disable endpoint | DELETE /api/v1/auth/mfa | Attempts to disable global MFA |
| Switch endpoint | POST /api/v1/auth/switch-tenant | Switches tenant context |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/auth/login` with TenantB context (beta subdomain) | HTTP 200 with `{ mfaChallenge: true, mfaMethod: "totp" }`. MFA is required by TenantB's policy. |
| 2 | Submit a valid TOTP code | HTTP 200 with tokens. Login to TenantB succeeds. |
| 3 | Send `POST /api/v1/auth/login` with TenantA context (alpha subdomain) | HTTP 200 with `{ mfaChallenge: true, mfaMethod: "totp" }`. Even though TenantA's policy is "off," the user has MFA enabled globally, so the system still challenges for MFA. |
| 4 | Submit a valid TOTP code for TenantA login | HTTP 200 with tokens. Login to TenantA succeeds. |
| 5 | Verify the TOTP secret is the same for both tenant logins | The `users.mfa_secret` is a single global value, not duplicated per tenant. |
| 6 | While authenticated in TenantA context, send `DELETE /api/v1/auth/mfa` | HTTP 403 with `{ error: "MFA cannot be disabled because it is required by another tenant (beta) for your role." }` or similar message referencing the blocking tenant. |
| 7 | Verify `mfa_enabled` is still `true` | MFA was not disabled. |
| 8 | Verify TOTP secret and recovery codes are intact | No data was cleared. |
| 9 | Change TenantB's policy to `mfaPolicy = "off"` (via TenantB admin) | Both tenants now have MFA off. |
| 10 | While authenticated in TenantA context, send `DELETE /api/v1/auth/mfa` again | HTTP 200. MFA is now disabled because no tenant requires it for the user's role. |
| 11 | Verify `mfa_enabled = false`, secret cleared, recovery codes deleted | Global MFA is fully disabled. |
| 12 | Send `POST /api/v1/auth/login` with TenantA context | Tokens issued directly after password stage. No MFA challenge. |
| 13 | Restore TenantB's policy to `mfaPolicy = "required"` | Reset for other tests. |

## 6. Postconditions
- Cross-tenant enforcement: user cannot disable MFA while any tenant requires it for their role.
- Global MFA secret is shared across tenants (single source of truth).
- When all requiring policies are removed, user can freely disable MFA.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
