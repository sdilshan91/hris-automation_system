---
id: TC-LV-181
user_story: US-LV-009
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-181: Team-calendar API response carries the documented item fields (FR-4)

## 1. Test Objective
Verify the `GET /api/v1/leaves/team-calendar` response items include the documented fields -- employeeId, employeeName, leaveTypeName, color, startDate, endDate, status, totalDays -- for a manager, and that the employee-context response correctly suppresses the sensitive subset (per BR-1).

## 2. Related Requirements
- User Story: US-LV-009
- Functional Requirements: FR-4, FR-1
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme"; Manager "Maya" and Employee "Nina" each authenticated; team leaves exist in range.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Manager fields | employeeId, employeeName, leaveTypeName, color, startDate, endDate, status, totalDays | FR-4 full set |
| Employee fields | employeeId, employeeName, startDate, endDate, totalDays, "on leave" marker | type/status suppressed |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Maya calls the team-calendar API | Each item contains all FR-4 fields with correct types (dates ISO date, totalDays decimal, status enum). |
| 2 | Verify `totalDays` for a half-day item | totalDays=0.5 for a half-day leave (consistent with BR-5 / TC-LV-178). |
| 3 | Nina calls the API | Items omit leaveTypeName/type-color/status (only the neutral on-leave subset), per BR-1 (cross-ref TC-LV-172). |
| 4 | Validate date range honoring | startDate/endDate of returned items overlap the requested from/to window. |

## 6. Postconditions
- API response field contract verified for both manager (full) and employee (suppressed) contexts.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
