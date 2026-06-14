---
id: TC-LV-144
user_story: US-LV-007
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-144: Tenant onboarding holiday seeding from a country template (FR-5, DEFERRED -- onboarding wizard UNWIRED)

## 1. Test Objective
Verify the FR-5 holiday-seeding capability: the holiday service can seed a tenant's calendar from a country-based static template, idempotently. The onboarding-wizard Step-4 call site is currently UNWIRED (TODO(onboarding)), so the wizard trigger is DEFERRED; this test verifies the seeding service behaviour directly and records the wiring gap honestly (not a silent pass).

## 2. Related Requirements
- User Story: US-LV-007
- Functional Requirements: FR-5
- Dependency: US-TENANT-* onboarding wizard (Step 4) -- call site UNWIRED
- Assumptions (Section 10): templates are static JSON in the application

## 3. Preconditions
- A fresh tenant "newco" with no holidays.
- A country-based holiday template available in the application (static JSON).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant | "newco" | no existing holidays |
| Template | a country template (e.g. US/UK) | static JSON |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Invoke the holiday seeding service for "newco" with the chosen country template | The template's holidays are created for "newco", tenant-scoped, with correct names/dates/types. |
| 2 | Re-invoke the seeding for "newco" | Idempotent: no duplicates created (skips when holidays already exist for the tenant). |
| 3 | (DEFERRED) Run the tenant onboarding wizard Step 4 | The wizard does NOT yet call the seeding service (TODO(onboarding) -- call site UNWIRED). Mark this trigger DEFERRED pending US-TENANT-*. |
| 4 | Record the deferral honestly | The seeding service + idempotency are verified now; the onboarding-wizard trigger is DEFERRED (call site unwired), not a coverage gap. |

## 6. Postconditions
- The seeding service populates a tenant's calendar idempotently; the onboarding-wizard trigger remains DEFERRED.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
