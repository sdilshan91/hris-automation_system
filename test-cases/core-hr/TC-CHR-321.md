---
id: TC-CHR-321
user_story: US-CHR-012
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-321: Multi-select dropdown stores array value in JSONB

## 1. Test Objective
Verify that a multi-select dropdown custom field stores an array of selected values in the employee's `custom_fields` JSONB column and that the values are correctly retrieved and rendered as selected chips/tags on the employee form.

## 2. Related Requirements
- User Story: US-CHR-012
- Functional Requirements: FR-2, FR-4, FR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Custom field "Skills" of type "Dropdown (multi-select)" with options ["JavaScript", "Python", "SQL", "Java", "Go"].
- HR Officer is authenticated.
- An employee record exists.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Custom Field | Skills | Multi-select dropdown |
| Selected Values | ["JavaScript", "SQL", "Go"] | 3 of 5 options |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to an employee's profile edit form, Custom Fields section. | The "Skills" field renders as a multi-select input showing all available options. |
| 2 | Select "JavaScript", "SQL", and "Go". | Three chips/tags appear showing the selected options. |
| 3 | Save the employee profile. | Success toast. |
| 4 | Query via API: `GET /api/v1/tenant/employees/{id}`. | The response contains `custom_fields: {"skills": ["JavaScript", "SQL", "Go"]}` -- an array in JSONB. |
| 5 | Reload the employee profile edit form. | The "Skills" field shows "JavaScript", "SQL", and "Go" as pre-selected chips. |
| 6 | Deselect "SQL" and save. | Success. API response shows `custom_fields: {"skills": ["JavaScript", "Go"]}`. |

## 6. Postconditions
- Multi-select values are stored as arrays in JSONB and correctly round-trip through save/load.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
