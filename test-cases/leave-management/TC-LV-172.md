---
id: TC-LV-172
user_story: US-LV-009
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-172: Employee team-calendar API response payload excludes pending status and leave-type fields (AC-2, BR-1; server-side data-leak probe)

## 1. Test Objective
Verify the data-leak prevention is enforced server-side, not just hidden in the UI: the raw API response returned to an employee must omit pending entries entirely and must not carry `leaveTypeName`/type-revealing `color`/`status` detail for department colleagues. Confirms a curious employee cannot read sensitive data by inspecting the network response.

## 2. Related Requirements
- User Story: US-LV-009
- Acceptance Criteria: AC-2
- Functional Requirements: FR-3, FR-4
- Non-Functional Requirements: NFR-3
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme"; Employee "Nina" authenticated (employee context, no team-view permission).
- Department colleagues have a mix of Approved and Pending leaves of sensitive types (Sick, Maternity).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Request | GET /api/v1/leaves/team-calendar?from=2026-06-01&to=2026-06-30 (employee JWT) | inspect raw JSON |
| Forbidden in payload | leaveTypeName, type-specific color, status=Pending, reason | data-leak vectors |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call the API with Nina's token and capture the raw JSON | Only Approved department leaves are present; no Pending items appear in the array (filtered server-side, not client-side). |
| 2 | Inspect each returned item's fields | Items expose employeeId/employeeName/startDate/endDate/totalDays and a neutral "on leave" marker; `leaveTypeName` is null/absent and any `color` is the neutral on-leave color (not the type color). |
| 3 | Attempt to add `&status=pending` to the query as the employee | The parameter is ignored/rejected for employees (status filter is manager-only, FR-6); no pending data is returned. |
| 4 | Compare with the same request under a manager token | The manager response (TC-LV-169) DOES include pending + leaveTypeName, confirming the difference is enforced by role, server-side. |

## 6. Postconditions
- Server-side payload for employees contains no pending entries and no leave-type/reason detail; isolation is enforced at the API, not the client.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
