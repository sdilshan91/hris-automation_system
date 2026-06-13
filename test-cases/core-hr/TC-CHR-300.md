---
id: TC-CHR-300
user_story: US-CHR-012
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-300: Plan limit reached -- 6th field blocked with upgrade message (DEFERRED)

## 1. Test Objective
Verify that when a tenant on the Starter plan (limit: 5 custom fields per entity) has already created 5 custom fields, attempting to create a 6th field is blocked with the message: "You have reached the maximum number of custom fields (5) for your current plan. Upgrade to add more." This validates AC-4, FR-6, and BR-4.

**STATUS: DEFERRED** -- Plan-tier limit enforcement depends on the Subscription module which is not yet implemented. The test case is defined for execution once plan limits are wired.

## 2. Related Requirements
- User Story: US-CHR-012
- Acceptance Criteria: AC-4
- Functional Requirements: FR-6
- Business Rules: BR-4

## 3. Preconditions
- Tenant "acme" exists on the "Starter" plan with custom field limit = 5.
- 5 custom fields already exist for the Employee entity in this tenant.
- Tenant Admin is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Plan | Starter | Limit: 5 custom fields per entity |
| Existing fields | 5 | All 5 slots used |
| New field attempt | Emergency Contact Type (text) | 6th field |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Settings > Custom Fields. | The page shows 5 existing fields. The plan limit indicator shows "5 of 5 custom fields used" (progress bar full). |
| 2 | Click "Add Custom Field". | The system blocks the action. A message is displayed: "You have reached the maximum number of custom fields (5) for your current plan. Upgrade to add more." |
| 3 | Attempt creation via API: `POST /api/v1/tenant/custom-fields` with valid body. | API returns HTTP 403 or 422 with the same upgrade message. |
| 4 | Verify no new field was created via `GET /api/v1/tenant/custom-fields?entityType=Employee`. | Still 5 fields. |

## 6. Postconditions
- No 6th field is created. The existing 5 fields remain unchanged.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
