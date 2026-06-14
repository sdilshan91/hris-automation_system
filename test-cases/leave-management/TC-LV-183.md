---
id: TC-LV-183
user_story: US-LV-009
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-183: Authentication and authorization on the team-calendar endpoint (NFR-3; unauthenticated denied)

## 1. Test Objective
Verify the team-calendar endpoint requires authentication (401 when unauthenticated) and enforces role-appropriate scope: an authenticated employee gets the restricted department-approved view, a manager gets the direct-report view, and `Leave.ViewAll` gets org-wide -- with no privilege escalation by tampering.

## 2. Related Requirements
- User Story: US-LV-009
- Non-Functional Requirements: NFR-3
- Business Rules: BR-1, BR-2, BR-3
- Cross-reference: US-AUTH-* (JWT auth)

## 3. Preconditions
- Tenant "acme"; users: unauthenticated client, Employee "Nina", Manager "Maya", HR Officer "Priya" (Leave.ViewAll).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| No token | -> 401 | unauthenticated denied |
| Employee token | restricted view | no pending/type |
| Tampered JWT | -> 401 | invalid signature |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call GET /api/v1/leaves/team-calendar with no Authorization header | 401 Unauthorized; no calendar data returned. |
| 2 | Call with an expired/tampered JWT | 401 Unauthorized (signature/expiry rejected). |
| 3 | Call as Nina (employee) | 200 with the restricted department-approved-only view (no pending, no leave-type), enforced server-side. |
| 4 | Nina attempts to forge a higher scope (e.g. crafted role claim or `?scope=all`) | The server derives scope from the validated identity/permissions, not the request; Nina cannot obtain the manager/HR view. |

## 6. Postconditions
- Endpoint denies unauthenticated/invalid tokens and binds scope to the authenticated principal's role/permissions.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
