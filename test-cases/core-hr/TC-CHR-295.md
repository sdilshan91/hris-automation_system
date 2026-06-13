---
id: TC-CHR-295
user_story: US-CHR-012
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-295: Create a "T-Shirt Size" dropdown custom field -- happy path

## 1. Test Objective
Verify that a Tenant Admin can navigate to Settings > Custom Fields, create a new dropdown custom field named "T-Shirt Size" with options ["S", "M", "L", "XL"], mark it as optional, and that the field definition is stored tenant-scoped. After creation, the field must immediately appear on both the employee creation form (US-CHR-001) and the employee profile edit form (US-CHR-002) within a "Custom Fields" or "Additional Information" section. This validates AC-1, AC-2, FR-1, FR-2, FR-3, and FR-9.

## 2. Related Requirements
- User Story: US-CHR-012
- Acceptance Criteria: AC-1, AC-2
- Functional Requirements: FR-1, FR-2, FR-3, FR-9
- Non-Functional Requirements: NFR-5

## 3. Preconditions
- Tenant "acme" exists with status `active` and subdomain `acme.yourhrm.com`.
- A Tenant Admin user is authenticated in the "acme" tenant context.
- No custom fields have been defined for the "Employee" entity in this tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Authorized persona per US-CHR-012 |
| Field Name | T-Shirt Size | Unique within tenant + Employee entity |
| Field Key | tshirt_size | Auto-generated slug |
| Field Type | Dropdown (single select) | One of the supported types (FR-2) |
| Options | ["S", "M", "L", "XL"] | Dropdown values |
| Is Required | false | Optional field |
| Entity Type | Employee | Phase 1 entity |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Settings > Custom Fields page. | The custom fields management page loads. An "Add Custom Field" button is visible. The page shows an empty state or existing fields grouped by entity (Employee). |
| 2 | Click "Add Custom Field". | A slide-over panel or modal opens with fields: Field Name, Field Key (auto-generated), Field Type (visual selector), Required toggle, Options (for dropdown), Display Order. |
| 3 | Enter "T-Shirt Size" as the field name. | The Field Key auto-generates as "tshirt_size" (URL-safe slug). |
| 4 | Select "Dropdown (single select)" as the field type. | An options input appears (tag-input component for entering dropdown values). |
| 5 | Add options: type "S" and press Enter, then "M", "L", "XL". | Four option chips appear: S, M, L, XL. Each is removable. |
| 6 | Leave the Required toggle off (optional). | The is_required field is set to false. |
| 7 | Save the custom field definition. | A success toast appears. The field appears in the custom fields list showing: name "T-Shirt Size", type "Dropdown", required "No", and usage count "0". |
| 8 | Verify via API: `GET /api/v1/tenant/custom-fields?entityType=Employee`. | The response includes the new field definition with `field_name: "T-Shirt Size"`, `field_key: "tshirt_size"`, `field_type: "dropdown"`, `options: ["S","M","L","XL"]`, `is_required: false`, `is_active: true`, `tenant_id` matching acme tenant. |
| 9 | Navigate to the employee creation form (Add New Employee). | The form includes a "Custom Fields" or "Additional Information" section containing a dropdown labeled "T-Shirt Size" with options S, M, L, XL. |
| 10 | Navigate to an existing employee's profile edit form. | The profile edit form includes the "T-Shirt Size" dropdown in the Custom Fields section. |

## 6. Postconditions
- One custom field definition exists for the "acme" tenant, entity type "Employee".
- The field is visible on both employee creation and employee profile edit forms.
- An audit log entry records the custom field creation.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
