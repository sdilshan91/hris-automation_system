---
id: TC-CHR-184
user_story: US-CHR-007
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-184: Employee count displayed per location with clickable badge

## 1. Test Objective
Verify that the Locations management page displays an accurate employee count per location as a clickable badge. The badge should link to the employee directory filtered by that location. This validates FR-7.

## 2. Related Requirements
- User Story: US-CHR-007
- Functional Requirements: FR-7
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- Location "Main Office" has 5 active employees assigned.
- Location "Remote" has 0 employees assigned.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Full access |
| Location "Main Office" | 5 employees | Active employees |
| Location "Remote" | 0 employees | No employees |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Locations management page | Locations list loads with card-based table. |
| 2 | Verify "Main Office" row shows Employee Count badge | A badge displays "5" (or "5 employees"). The badge is styled as clickable (e.g., underline, pointer cursor, link color). |
| 3 | Verify "Remote" row shows Employee Count badge | A badge displays "0". |
| 4 | Click the employee count badge on "Main Office" | Browser navigates to the Employee Directory filtered by location "Main Office" (e.g., `/employees?location=Main+Office` or equivalent). |
| 5 | Verify the directory shows exactly 5 employees | The filtered directory displays the 5 employees assigned to "Main Office". |
| 6 | Navigate back to the Locations page | Employee counts are still accurate. |

## 6. Postconditions
- No data was modified.
- Employee counts accurately reflect current assignments.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
