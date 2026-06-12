---
id: TC-CHR-153
user_story: US-CHR-006
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-153: Toggle to reporting structure view shows manager-to-direct-report relationships

## 1. Test Objective
Verify that toggling to the "Reporting Structure" view reorganizes the tree to show manager-to-direct-report relationships (people-centric rather than department-centric), with each manager node expandable to show their reports. This validates AC-3, FR-1.

## 2. Related Requirements
- User Story: US-CHR-006
- Acceptance Criteria: AC-3
- Functional Requirements: FR-1, FR-2
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in the "acme" tenant context.
- Manager assignments configured: "Alice Adams" manages "Bob Baker" and "Carol Chen". "Bob Baker" manages "Dave Daniels" and "Eve Evans".
- "Frank Foster" has no manager assigned but belongs to the "Engineering" department.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Top Manager | Alice Adams | Root in reporting structure |
| Direct Reports of Alice | Bob Baker, Carol Chen | Level 2 |
| Direct Reports of Bob | Dave Daniels, Eve Evans | Level 3 |
| Unmanaged Employee | Frank Foster | No manager; belongs to Engineering department |
| API Endpoint | GET /api/v1/org-tree?view=reporting&depth=2 | Reporting view |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Organization Tree page | Org chart renders in default "Department" view. |
| 2 | Click the "Reporting" toggle button in the segmented control | The view switches with smooth animation; the segmented control indicator moves to "Reporting". |
| 3 | Verify the API call `GET /api/v1/org-tree?view=reporting&depth=2` was made | Response status 200 OK; response contains person nodes with manager-report relationships. |
| 4 | Verify the root node is "Alice Adams" | Alice Adams appears at the top of the tree with avatar, name, and job title. |
| 5 | Verify "Bob Baker" and "Carol Chen" are shown as direct reports of Alice | Two child nodes branch downward from Alice's node, connected by SVG paths. |
| 6 | Expand "Bob Baker" node | "Dave Daniels" and "Eve Evans" appear as Bob's direct reports (lazy-loaded if depth > 2). |
| 7 | Verify "Frank Foster" appears under the Engineering department node, not under any manager | Since Frank has no manager, he appears under his department node (per BR-3) but not in any manager's report chain. |
| 8 | Toggle back to "Department" view | The tree reorganizes back to the department hierarchy layout. |

## 6. Postconditions
- No data was modified.
- View toggle state is reflected in the UI.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
