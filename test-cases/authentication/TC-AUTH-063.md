---
id: TC-AUTH-063
user_story: US-AUTH-008
module: Authentication
priority: high
type: functional
status: draft
created: 2026-06-09
---

# TC-AUTH-063: Source session remains valid and MFA-required target triggers enrollment

## 1. Test Objective
Verify that switching preserves the source tenant refresh session and that a target tenant requiring MFA triggers mandatory enrollment when the user has not enrolled.

## 2. Related Requirements
- User Story: US-AUTH-008
- Acceptance Criteria: AC-2, AC-5
- Functional Requirements: FR-3, FR-4, FR-6, FR-8
- Business Rules: BR-1, BR-3

## 3. Preconditions
- User is authenticated in tenant A with an active refresh token.
- User has active membership in tenant B.
- Tenant B requires MFA for the user's target role.
- The user has not enrolled in MFA.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Source tenant | acme | MFA optional |
| Target tenant | globex | MFA required for Employee role |
| User MFA state | Not enrolled | Must trigger enrollment |
| Redirect after switch | https://globex.yourhrm.com/dashboard or MFA enrollment route | Depends on policy flow |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Capture tenant A refresh token/session state before switching. | Tenant A session is valid. |
| 2 | Call `POST /api/v1/auth/switch-tenant` for tenant B. | Switch succeeds enough to establish target context, or returns a structured MFA-required response. |
| 3 | Inspect target response. | Response directs user to mandatory MFA enrollment for tenant B before dashboard access. |
| 4 | Attempt to access tenant B dashboard without completing MFA enrollment. | Access is blocked or redirected to MFA enrollment. |
| 5 | Complete MFA enrollment and verification for tenant B. | Tenant B dashboard becomes accessible with target tenant context. |
| 6 | Navigate back to tenant A subdomain and use tenant A refresh flow if needed. | Tenant A session remains valid and user can return without entering credentials. |
| 7 | Verify tenant B JWT after MFA completion. | JWT contains tenant B roles/permissions only. |
| 8 | Verify audit trail. | Tenant switch and MFA enrollment/challenge events are recorded with correct tenant context. |

## 6. Postconditions
- Source tenant refresh token remains valid.
- Target tenant enforces MFA enrollment before protected access.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
