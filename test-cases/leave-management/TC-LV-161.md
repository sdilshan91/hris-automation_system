---
id: TC-LV-161
user_story: US-LV-008
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-161: Preview API authorization -- only leave-config permission holders may preview carry-forward (AC-5, NFR-2)

## 1. Test Objective
Verify role-based access on the carry-forward preview endpoint: only users with the leave-configuration permission (e.g. `Leave.ConfigurePolicy` / Tenant Admin / HR) may call `GET /api/v1/leaves/carry-forward-preview`; a plain employee or a user lacking the permission is rejected with 403 (AC-5, NFR-2).

## 2. Related Requirements
- User Story: US-LV-008
- Acceptance Criteria: AC-5
- Non-Functional Requirements: NFR-2
- Cross-reference: US-AUTH-006 (RBAC)

## 3. Preconditions
- Tenant "acme" with: HR Officer "Priya" (leave-config permission), employee "Sam" (no leave-config permission), and "Guest" (no leave permissions).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Endpoint | GET /api/v1/leaves/carry-forward-preview?year=2026 | config permission required |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Priya (leave-config permission) calls the preview endpoint | 200 OK with the projection (authorized). |
| 2 | Sam (employee, no leave-config permission) calls the preview endpoint | 403 Forbidden -- lacks the permission. |
| 3 | "Guest" (no leave permissions) calls the preview endpoint | 403 Forbidden. |
| 4 | Confirm the preview leaks no other-tenant or unauthorized data on denial | The 403 response contains no projection data; nothing is computed or returned for unauthorized callers. |

## 6. Postconditions
- The carry-forward preview is restricted to leave-config permission holders.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
