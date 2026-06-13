---
id: TC-CHR-303
user_story: US-CHR-012
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-303: Duplicate field name within tenant+entity rejected

## 1. Test Objective
Verify that the system rejects the creation of a custom field with a name that already exists for the same tenant and entity type combination. This validates BR-1 (unique field names within tenant + entity).

## 2. Related Requirements
- User Story: US-CHR-012
- Business Rules: BR-1
- Functional Requirements: FR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Custom field "T-Shirt Size" already exists for the Employee entity in tenant "acme".
- Tenant Admin is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Existing Field | T-Shirt Size | Already exists for acme/Employee |
| Duplicate Attempt | T-Shirt Size | Same name, same tenant, same entity |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Settings > Custom Fields. Click "Add Custom Field". | The creation form opens. |
| 2 | Enter "T-Shirt Size" as the field name. Select any field type. Attempt to save. | The system rejects with error: "A custom field named 'T-Shirt Size' already exists for this entity type." (or similar). |
| 3 | Attempt via API: `POST /api/v1/tenant/custom-fields` with `field_name: "T-Shirt Size"`, `entity_type: "employee"`. | API returns HTTP 409 or 422 with a uniqueness constraint error. |
| 4 | Change the name to "T-Shirt Size 2". Save. | The field is created successfully -- different names are allowed. |

## 6. Postconditions
- No duplicate field names exist within the same tenant + entity combination.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
