---
id: TC-CHR-171
user_story: US-CHR-006
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-171: Org tree reflects current state -- not historical snapshots

## 1. Test Objective
Verify that the org tree shows the current live state of department hierarchy and manager assignments, not historical snapshots. When a department is reassigned or a manager changes, the tree reflects the change on next load. This validates BR-1.

## 2. Related Requirements
- User Story: US-CHR-006
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in the "acme" tenant context.
- Department "Backend" is currently a child of "Engineering" with manager "Alice Adams".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Department | Backend | Child of Engineering |
| Original Manager | Alice Adams | Current manager |
| New Manager | Bob Baker | Will be reassigned |
| New Parent | Sales | Backend moved from Engineering to Sales |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Organization Tree page | "Backend" appears as a child of "Engineering" with "Alice Adams" as manager. |
| 2 | In a separate tab, navigate to Department Management and change "Backend" parent from "Engineering" to "Sales" | Change is saved successfully. |
| 3 | In a separate tab, change "Backend" manager from "Alice Adams" to "Bob Baker" | Change is saved successfully. |
| 4 | Return to the Organization Tree page and refresh | The tree re-fetches data from the API. |
| 5 | Verify "Backend" is now under "Sales" | "Backend" is rendered as a child of "Sales", not "Engineering". Connector line goes from "Sales" to "Backend". |
| 6 | Verify "Backend" manager is now "Bob Baker" | The "Backend" node card shows "Bob Baker" as manager with Bob's avatar. |
| 7 | Verify no historical state of "Engineering" -> "Backend" is displayed | "Engineering" no longer shows "Backend" as a child. No visual indicator of the previous relationship exists on this page. |

## 6. Postconditions
- The org tree accurately reflects the current database state.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
