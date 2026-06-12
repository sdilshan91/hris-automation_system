---
id: TC-CHR-028
user_story: US-CHR-004
module: Core HR
priority: high
type: performance
status: draft
created: 2026-06-11
---

# TC-CHR-028: Support 500 departments per tenant without degradation

## 1. Test Objective
Verify that the system supports up to 500 departments per tenant without performance degradation, per NFR-4. All CRUD operations, list rendering, and tree view must remain within acceptable thresholds.

## 2. Related Requirements
- User Story: US-CHR-004
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated.
- 500 departments are pre-seeded in "acme" with varying hierarchy depths.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Department Count | 500 | Maximum expected per NFR-4 |
| Hierarchy Levels | Mixed (1-8 levels) | Realistic distribution |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/departments` and verify all 500 departments are returned | Response contains 500 items. Response time P95 <= 400ms. |
| 2 | Send `GET /api/v1/departments/tree` and verify the full tree is returned | Tree data contains all 500 departments with correct hierarchy. Response time P95 <= 400ms. |
| 3 | Navigate to the department list page in the UI | Page loads and displays all departments (with pagination if implemented). No browser freeze or crash. |
| 4 | Toggle to tree view | Tree renders without browser freeze. Expand/collapse remains responsive. |
| 5 | Create department #501 | Creation succeeds. Response time <= 800ms. |
| 6 | Search/filter departments in the list | Filter results appear within 500ms. |

## 6. Postconditions
- System handles 500+ departments per tenant without degradation.
- UI remains responsive.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
