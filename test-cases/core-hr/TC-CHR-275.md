---
id: TC-CHR-275
user_story: US-CHR-011
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-275: Inactive/terminated employee cannot be assigned as manager

## 1. Test Objective
Verify that only employees with `active` status can be assigned as reporting managers. Assigning a terminated or suspended employee as a manager is rejected. This validates BR-3.

## 2. Related Requirements
- User Story: US-CHR-011
- Business Rules: BR-3
- Preconditions: Section 2 ("manager employee must have active status")

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated.
- Employee E exists with status `active`.
- Employee T exists with status `terminated`.
- Employee S exists with status `suspended`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee E | emp@acme.test | Active, needs a manager |
| Terminated T | term@acme.test | Status: terminated |
| Suspended S | susp@acme.test | Status: suspended |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Employee E's profile, edit Reporting Manager. | Manager selector opens. |
| 2 | Search for Employee T (terminated) in the autocomplete. | Employee T does NOT appear in the search results (UI filters to active employees only). |
| 3 | Search for Employee S (suspended) in the autocomplete. | Employee S does NOT appear in the search results. |
| 4 | Attempt via API: assign `reports_to_employee_id` = T.id on Employee E. | API returns 400 Bad Request with message indicating only active employees can be assigned as managers. |
| 5 | Attempt via API: assign `reports_to_employee_id` = S.id on Employee E. | API returns 400 Bad Request with the same inactive-manager error. |
| 6 | Verify Employee E's record is unchanged. | `reports_to_employee_id` remains null (or previous value). |

## 6. Postconditions
- No state change. Employee E's manager is not modified.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
