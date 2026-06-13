---
id: TC-CHR-322
user_story: US-CHR-012
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-322: Checkbox boolean custom field stores true/false in JSONB

## 1. Test Objective
Verify that a checkbox (boolean) custom field stores `true` or `false` in the employee's `custom_fields` JSONB column and renders as a toggle or checkbox on the employee form.

## 2. Related Requirements
- User Story: US-CHR-012
- Functional Requirements: FR-2, FR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Custom field "Has Parking Pass" of type "Checkbox" exists.
- HR Officer is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Custom Field | Has Parking Pass | Type: checkbox (boolean) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to employee creation form. Locate "Has Parking Pass" in Custom Fields. | The field renders as a toggle/checkbox, defaulting to unchecked. |
| 2 | Check the toggle/checkbox (set to true). Save the employee. | Employee created. |
| 3 | Query via API. | `custom_fields: {"has_parking_pass": true}` in JSONB. |
| 4 | Edit the employee. Uncheck the toggle. Save. | Success. |
| 5 | Query via API. | `custom_fields: {"has_parking_pass": false}` in JSONB. |
| 6 | Reload the employee profile. | The checkbox shows unchecked state. |

## 6. Postconditions
- Boolean values are correctly stored and retrieved from JSONB.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
