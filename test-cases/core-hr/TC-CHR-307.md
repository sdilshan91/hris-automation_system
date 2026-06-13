---
id: TC-CHR-307
user_story: US-CHR-012
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-307: Field type immutable after data exists

## 1. Test Objective
Verify that a custom field's type cannot be changed once employee data exists for that field. The Tenant Admin must deactivate the field and create a new one if a type change is needed. This prevents data corruption. This validates BR-5.

## 2. Related Requirements
- User Story: US-CHR-012
- Business Rules: BR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Custom field "T-Shirt Size" of type "Dropdown" exists.
- At least one employee has a value stored for this field (e.g., tshirt_size = "L").
- Tenant Admin is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Custom Field | T-Shirt Size | Type: dropdown, has data |
| Employee with data | Jane Smith | tshirt_size: "L" |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Settings > Custom Fields. Click edit on "T-Shirt Size". | The edit form opens. The Field Type selector is disabled/greyed out with a tooltip: "Field type cannot be changed because data exists. Deactivate this field and create a new one." (or similar). |
| 2 | Attempt to change the field type via API: `PUT /api/v1/tenant/custom-fields/{id}` with `field_type: "text"`. | API returns HTTP 400/422: "Cannot change field type after data exists for this field." |
| 3 | Create a new custom field with no data. Navigate to its edit form. | The Field Type selector is enabled (no data exists yet). |
| 4 | Change the new field's type from "Text" to "Number". Save. | Success -- type change allowed because no data exists for this field yet. |

## 6. Postconditions
- Fields with existing data retain their original type.
- Fields without data can have their type changed.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
