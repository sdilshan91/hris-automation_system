---
id: TC-CHR-319
user_story: US-CHR-012
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-319: field_key auto-generated from field name and immutable after creation

## 1. Test Objective
Verify that the `field_key` is auto-generated as a URL-safe slug from the field name during creation, is editable before save, and becomes immutable after the field is created. The field_key is used as the JSONB key and must remain stable. This validates the constraint in Section 10 (Assumptions).

## 2. Related Requirements
- User Story: US-CHR-012
- Data Requirements: Section 7 (field_key column)
- Assumptions: Section 10

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Tenant Admin is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Field Name | T-Shirt Size | Input by admin |
| Expected Key | tshirt_size | Auto-generated slug |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open "Add Custom Field" form. Enter "T-Shirt Size" as the name. | The field_key auto-populates as "tshirt_size" (lowercase, hyphens/spaces replaced with underscores, URL-safe). |
| 2 | Optionally edit the field_key to "tee_size" before saving. | The field_key accepts the manual override. |
| 3 | Save the custom field. | Field created with field_key = "tee_size". |
| 4 | Edit the custom field definition. Attempt to change the field_key. | The field_key input is disabled/greyed out. It cannot be changed after creation. |
| 5 | Attempt via API: `PUT /api/v1/tenant/custom-fields/{id}` with `field_key: "new_key"`. | The API ignores the field_key change or returns a validation error. The stored key remains "tee_size". |
| 6 | Create an employee with the custom field value. Check JSONB. | The JSONB uses the field_key: `{"tee_size": "L"}`. |

## 6. Postconditions
- field_key is immutable after creation. JSONB keys remain consistent.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
