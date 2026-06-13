---
id: TC-LV-116
user_story: US-LV-006
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-116: Only active leave types shown; deactivated type with remaining balance appears in collapsed Archived section

## 1. Test Objective
Verify that the main dashboard grid shows only active leave types, while a deactivated leave type that still has a remaining balance is shown in a collapsed "Archived" section rather than hidden entirely (BR-3, FR-1).

## 2. Related Requirements
- User Story: US-LV-006
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" active; employee "Nina Patel" authenticated.
- "Annual" and "Sick" are active. "Wellness Leave" was deactivated mid-year but Nina retains a remaining balance of 1.5 days.
- A second deactivated type "Legacy Leave" has zero remaining balance.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Active types | Annual, Sick | Main grid |
| Deactivated, balance 1.5 | Wellness Leave | Archived section |
| Deactivated, balance 0 | Legacy Leave | Not shown |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the dashboard | The main grid shows only the active types (Annual, Sick); deactivated types are not in the main grid. |
| 2 | Locate the "Archived" section | An Archived section is present and collapsed by default, with a count/affordance to expand. |
| 3 | Expand Archived | Wellness Leave (remaining balance 1.5) is listed; its card is visually marked archived/read-only. |
| 4 | Verify zero-balance exclusion | Legacy Leave (balance 0) is NOT shown in either the main grid or the Archived section. |

## 6. Postconditions
- Active vs archived separation respected; only deactivated-with-balance types appear under Archived.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
