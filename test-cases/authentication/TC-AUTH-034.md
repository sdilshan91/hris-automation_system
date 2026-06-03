---
id: TC-AUTH-034
user_story: US-AUTH-005
module: Authentication
priority: high
type: security
status: draft
created: 2026-06-03
---

# TC-AUTH-034: Disable MFA blocked when tenant policy requires it for user's role

## 1. Test Objective
Verify that a user whose role is listed in the tenant's `mfaRequiredRoles` cannot disable MFA. The system must return a 403 error with an explanatory message, and MFA must remain enabled.

## 2. Related Requirements
- User Story: US-AUTH-005
- Acceptance Criteria: AC-1, AC-6 (negative path)
- Functional Requirements: FR-9
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" has `mfaPolicy = "required"`, `mfaRequiredRoles = ["Tenant Admin"]`.
- User `admin@acme.com` has the "Tenant Admin" role and `mfa_enabled = true`.
- User is authenticated with a valid JWT token.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| User | admin@acme.com | Tenant Admin role, MFA enabled |
| Tenant | acme | mfaPolicy=required, mfaRequiredRoles=["Tenant Admin"] |
| Disable endpoint | DELETE /api/v1/auth/mfa | Attempts to disable MFA |
| Expected error | 403 | "MFA is required by tenant policy for your role and cannot be disabled." |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Confirm user's current state: `mfa_enabled = true`, valid TOTP secret, 10 recovery codes | MFA is fully active. |
| 2 | Send `DELETE /api/v1/auth/mfa` as `admin@acme.com` | HTTP 403 with `{ error: "MFA is required by tenant policy for your role and cannot be disabled." }`. |
| 3 | Verify `users.mfa_enabled` is still `true` | MFA was not disabled. |
| 4 | Verify `users.mfa_secret` is still populated (encrypted) | Secret was not cleared. |
| 5 | Verify recovery codes still exist in `mfa_recovery_code` table | No codes were deleted. |
| 6 | Verify no `mfa_disabled` audit event was logged | The failed attempt does not create a misleading audit record. |
| 7 | Change tenant policy to `mfaPolicy = "optional"` via admin endpoint | Policy updated successfully. |
| 8 | Send `DELETE /api/v1/auth/mfa` as `admin@acme.com` again | HTTP 200. MFA is now disabled because the policy no longer requires it. |
| 9 | Verify `mfa_enabled = false` and secret is cleared | Disable succeeds under optional policy. |
| 10 | Revert tenant policy to `mfaPolicy = "required"` | Restore original state for other tests. |

## 6. Postconditions
- Under required policy: MFA remains enabled; disable attempt is rejected.
- Under optional policy: MFA can be disabled normally (verified as control).
- No spurious audit events from the rejected attempt.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
