---
id: TC-CHR-151
user_story: US-CHR-006
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-151: Department hierarchy tree renders with correct parent-child relationships and employee counts

## 1. Test Objective
Verify that when an HR Officer navigates to the Organization Tree page, an interactive org chart is rendered showing the department hierarchy with department names, manager avatars/names, and employee counts per node. Root departments appear at the top with child departments branching downward. This validates AC-1, FR-1, FR-5.

## 2. Related Requirements
- User Story: US-CHR-006
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1, FR-2, FR-5, FR-8
- Non-Functional Requirements: NFR-3
- Business Rules: BR-1, BR-2

## 3. Preconditions
- Tenant "acme" exists with status `active` and subdomain `acme.yourhrm.com`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- Department hierarchy configured: "Engineering" (root) -> "Backend" (child) -> "Platform" (grandchild).
- "Engineering" has manager "Alice Adams" and 15 total employees.
- "Backend" has manager "Bob Baker" and 8 employees.
- "Platform" has manager "Carol Chen" and 3 employees.
- An additional root department "HR" exists with manager "Dave Daniels" and 5 employees.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Full access |
| Root Departments | Engineering, HR | Two root nodes |
| Child of Engineering | Backend | Level 2 |
| Child of Backend | Platform | Level 3 |
| API Endpoint | GET /api/v1/org-tree?view=department&depth=2 | Initial load |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `https://acme.yourhrm.com/org-tree` | Organization Tree page begins loading. |
| 2 | Wait for page load to complete | An interactive org chart canvas is rendered with node cards. |
| 3 | Verify root-level nodes | Two root nodes are visible at the top level: "Engineering" and "HR". Both are positioned at the topmost row of the chart. |
| 4 | Verify "Engineering" node card content | Card shows: manager avatar (32px circle for Alice Adams), "Engineering" name, "Alice Adams" as manager, employee count badge "15". |
| 5 | Verify "HR" node card content | Card shows: manager avatar for Dave Daniels, "HR" name, "Dave Daniels" as manager, employee count badge "5". |
| 6 | Verify connector lines from "Engineering" to "Backend" | A smooth curved SVG path connects "Engineering" to "Backend" downward. |
| 7 | Verify "Backend" node card content at level 2 | Card shows: "Backend" name, "Bob Baker" as manager, employee count badge "8". Node is indented/positioned below "Engineering". |
| 8 | Verify "Platform" is not yet visible (lazy loading -- only top 2 levels) | "Platform" node is not rendered until "Backend" is expanded. |
| 9 | Verify view toggle buttons at the top | Segmented control with "Department" and "Reporting" buttons is visible. "Department" is active/selected. |
| 10 | Verify search bar is present | Search bar with typeahead is visible at the top-right. |
| 11 | Verify zoom controls | Floating toolbar with "+", "-", and "Fit to screen" buttons is visible. |
| 12 | Verify the API call `GET /api/v1/org-tree?view=department&depth=2` was made | Response status 200 OK; response contains department nodes with employee counts. |

## 6. Postconditions
- No data was modified.
- The org chart is displayed in department hierarchy view by default.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
