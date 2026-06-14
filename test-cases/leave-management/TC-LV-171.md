---
id: TC-LV-171
user_story: US-LV-009
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-171: Employee view shows only APPROVED department leaves, WITHOUT pending and WITHOUT leave-type detail -- "on leave" only (AC-2, BR-1; KEY access control)

## 1. Test Objective
Verify the KEY access-control rule: when a non-manager Employee opens the Team Leave Calendar, they see ONLY approved leaves of their own department, rendered as a neutral "on leave" block, with NO pending requests and NO leave-type detail exposed (data-leak prevention for sensitive leave reasons such as sick/maternity).

## 2. Related Requirements
- User Story: US-LV-009
- Acceptance Criteria: AC-2
- Functional Requirements: FR-3
- Non-Functional Requirements: NFR-3
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme"; Employee "Nina" (no manager/team-view permission), department "Engineering".
- Department colleague "Sam": Annual Leave Approved 2026-06-08..10; colleague "Ravi": Sick Leave Pending 2026-06-15.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Endpoint | GET /api/v1/leaves/team-calendar?from=2026-06-01&to=2026-06-30 | employee context |
| Sam (Approved) | shown as "on leave" | NO leaveTypeName, NO color leaking type |
| Ravi (Pending) | NOT shown | pending hidden from employees |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Nina opens the Team Leave Calendar | The calendar loads showing department-level entries only. |
| 2 | Inspect Sam's approved leave | Sam appears "on leave" on 2026-06-08..10 as a neutral block; the leave type (Annual) is NOT displayed and the block does not reveal type via a type-specific color/label. |
| 3 | Look for Ravi's pending leave | Ravi's Pending Sick leave does NOT appear at all on the employee calendar (FR-3, BR-1). |
| 4 | Open any tooltip/detail an employee can reach | Tooltip shows only employee name + "On leave" + dates; no leaveTypeName, no status=Pending, no reason. |

## 6. Postconditions
- Employee sees department approved leaves as "on leave" with no pending and no leave-type detail; sensitive data is not leaked.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
