---
id: TC-AUTH-062
user_story: US-AUTH-008
module: Authentication
priority: critical
type: security
status: draft
created: 2026-06-09
---

# TC-AUTH-062: Tenant switch blocks impersonation and prevents cross-tenant data exposure

## 1. Test Objective
Verify that impersonation sessions cannot switch tenants and that successful or failed switch responses do not expose source-tenant business data.

## 2. Related Requirements
- User Story: US-AUTH-008
- Acceptance Criteria: AC-2, AC-3, AC-5
- Functional Requirements: FR-3, FR-5, FR-7
- Business Rules: BR-4
- Non-Functional Requirements: NFR-3, NFR-4

## 3. Preconditions
- System support user is impersonating `multi.user@yourhrm.test` in tenant A.
- The impersonated user has active membership in tenant B.
- Source tenant A contains employees, payroll runs, and audit entries not present in tenant B.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Impersonator | support@system.yourhrm.test | System support role |
| Impersonated user | multi.user@yourhrm.test | Member of tenant A and B |
| Source tenant | acme | Contains private source records |
| Target tenant | globex | Target switch tenant |
| Switch endpoint | POST /api/v1/auth/switch-tenant | Called during impersonation |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Start a valid impersonation session for the user in tenant A. | Impersonation token/session is active and clearly marked as impersonated. |
| 2 | Call `GET /api/v1/auth/my-tenants` during impersonation. | Response is either blocked or excludes switch actions according to product decision; it does not enable switching. |
| 3 | Call `POST /api/v1/auth/switch-tenant` with tenant B ID while impersonating. | HTTP 403 with impersonation-not-allowed error; no target access token is issued. |
| 4 | End impersonation and authenticate normally as the user in tenant A. | Normal user session is active. |
| 5 | Switch normally to tenant B. | HTTP 200 with tenant B token. |
| 6 | Inspect switch response body and JWT. | Response includes only token, target tenant summary, and redirect URL; no source employee, payroll, audit, role configuration, or tenant A branding data is exposed. |
| 7 | Use tenant B JWT to call representative tenant-scoped APIs. | Results contain tenant B data only. |
| 8 | Attempt to use tenant B JWT with tenant A host/header context. | API rejects the mismatched tenant context. |

## 6. Postconditions
- Impersonation session remains unable to switch tenants.
- Normal switch responses and follow-up APIs expose only target tenant data.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
