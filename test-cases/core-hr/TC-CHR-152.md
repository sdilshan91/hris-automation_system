---
id: TC-CHR-152
user_story: US-CHR-006
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-152: Click department node opens detail panel with manager, employees, and sub-departments

## 1. Test Objective
Verify that clicking on a department node in the org tree opens a detail panel or expanded view showing the department manager, list of direct employees, sub-departments, and a link to the department management page. This validates AC-2.

## 2. Related Requirements
- User Story: US-CHR-006
- Acceptance Criteria: AC-2
- Functional Requirements: FR-1, FR-2
- Business Rules: BR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in the "acme" tenant context.
- Department "Engineering" exists as root with manager "Alice Adams", sub-department "Backend", and direct employees: "Eve Evans", "Frank Foster", "Grace Green".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Department | Engineering | Root department |
| Manager | Alice Adams | Department manager |
| Sub-departments | Backend | One child department |
| Direct Employees | Eve Evans, Frank Foster, Grace Green | 3 direct employees |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Organization Tree page | Org chart renders with "Engineering" node visible. |
| 2 | Click on the "Engineering" department node | A detail panel or expanded view appears. |
| 3 | Verify the detail panel shows the department manager | "Alice Adams" is displayed with avatar, name, and title. |
| 4 | Verify the detail panel lists direct employees | "Eve Evans", "Frank Foster", "Grace Green" are listed with names and job titles. |
| 5 | Verify the detail panel shows sub-departments | "Backend" is listed as a sub-department. |
| 6 | Verify a link to the department management page is present | A link or button labeled "Manage Department" or similar navigates to `/departments/{engineering-id}`. |
| 7 | Click the department management link | Browser navigates to the department management page for "Engineering". |
| 8 | Navigate back to the org tree | The org tree re-renders in its previous state. |

## 6. Postconditions
- No data was modified.
- The detail panel can be dismissed by clicking elsewhere or a close button.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
