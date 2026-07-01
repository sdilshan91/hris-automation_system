---
id: TC-CHR-028
user_story: US-CHR-004
module: Core HR
priority: high
type: performance
status: blocked
created: 2026-06-11
exec_note: "P3b k6 2026-06-30: SCALE NOT SEEDED — kept blocked. Requires 500 departments per tenant + create #501 write + filter; the 5k perf tenant was seeded for employee volume, not 500-dept scale, and the create/filter write arms were not driven. Needs a 500-department volume seed. Perf harness exists (perf/), re-run after seeding."

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

> **Execution 2026-06-30 (FE, acme):** STILL BLOCKED — performance/SLA TC. The Departments page renders ~51 cards promptly in-browser, but this TC requires instrumented page-load timing (P95 / 500-dept scale seed) that the single shared Playwright browser cannot measure reliably. Not a functional defect. Needs perf harness + seeded scale data.
