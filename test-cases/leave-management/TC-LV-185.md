---
id: TC-LV-185
user_story: US-LV-009
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-185: Employee cannot escalate scope to view other departments or pending leaves via parameter tampering (AC-2, BR-1, BR-2)

## 1. Test Objective
Verify an authenticated employee cannot use query parameters to broaden their calendar beyond their own department's APPROVED leaves -- e.g. requesting another department, another employee outside their department, or forcing pending inclusion -- confirming the AC-2/BR-1 restriction is enforced server-side and immune to client manipulation.

## 2. Related Requirements
- User Story: US-LV-009
- Acceptance Criteria: AC-2
- Business Rules: BR-1, BR-2
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme"; Employee "Nina" in department "Engineering".
- Department "Finance" employee "Lena" has approved + pending leaves; "Ravi" (Engineering) has a pending Sick leave.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tamper 1 | ?departmentId={Finance id} | cross-department probe |
| Tamper 2 | ?employeeId={Lena id} | out-of-department employee |
| Tamper 3 | ?status=Pending / ?includePending=true | force pending |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Nina requests `?departmentId={Finance id}` | The server ignores/forbids the override and returns only Nina's own department's approved leaves (no Finance data). |
| 2 | Nina requests `?employeeId={Lena id}` | Returns empty / 403 for the out-of-scope employee; Lena's data is not exposed. |
| 3 | Nina requests `?status=Pending` or `?includePending=true` | Pending leaves are still excluded; the parameter cannot force pending into an employee view (Ravi's pending Sick stays hidden). |
| 4 | Nina requests her own department but inspects for leave-type | Even with valid department scope, leaveTypeName/type-color remains suppressed (BR-1). |

## 6. Postconditions
- Scope and pending/type suppression are enforced server-side; no employee parameter tampering escalates access.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
