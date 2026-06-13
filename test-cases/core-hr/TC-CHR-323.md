---
id: TC-CHR-323
user_story: US-CHR-012
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-323: Plan limit indicator displayed on management page (DEFERRED)

## 1. Test Objective
Verify that the Custom Fields management page displays a plan limit progress indicator showing the current usage count and the plan maximum (e.g., "3 of 5 custom fields used") as a progress bar near the top of the page. This validates the UI/UX notes.

**STATUS: DEFERRED** -- Plan-tier configuration depends on the Subscription module which is not yet implemented. The test case is defined for execution once plan limits are wired.

## 2. Related Requirements
- User Story: US-CHR-012
- Business Rules: BR-4
- Functional Requirements: FR-6
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" on "Starter" plan (limit: 5 custom fields per entity).
- 3 custom fields exist for the Employee entity.
- Tenant Admin is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Plan | Starter | Limit: 5 |
| Current usage | 3 | 3 fields defined |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Settings > Custom Fields. | The page shows a progress bar/indicator: "3 of 5 custom fields used". The bar is 60% filled. |
| 2 | Create a 4th field. | The indicator updates to "4 of 5 custom fields used" (80% filled). |
| 3 | Create a 5th field. | The indicator shows "5 of 5 custom fields used" (100% filled). The progress bar may change color to warn the admin. |
| 4 | Verify the "Add Custom Field" button is disabled or clicking it shows the upgrade message. | The admin is blocked from adding a 6th field. |

## 6. Postconditions
- The plan limit indicator correctly reflects current usage vs. limit.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
