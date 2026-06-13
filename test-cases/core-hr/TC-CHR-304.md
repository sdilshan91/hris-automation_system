---
id: TC-CHR-304
user_story: US-CHR-012
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-304: Deactivate custom field hides from forms but preserves JSONB data

## 1. Test Objective
Verify that when a Tenant Admin deactivates (toggles off) an existing custom field, the field is hidden from employee creation forms, employee profile edit forms, and the directory. Crucially, existing data stored in the employee `custom_fields` JSONB column must be preserved -- not deleted. This validates AC-5, FR-7, and BR-3.

## 2. Related Requirements
- User Story: US-CHR-012
- Acceptance Criteria: AC-5
- Functional Requirements: FR-7
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Custom field "T-Shirt Size" (dropdown) exists and is active.
- Employee "Jane Smith" has `custom_fields: {"tshirt_size": "L"}`.
- Tenant Admin is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Custom Field | T-Shirt Size | Active, with data on employees |
| Employee | Jane Smith | custom_fields contains tshirt_size: "L" |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Settings > Custom Fields. Locate "T-Shirt Size". | The field shows is_active = true with a toggle or deactivate button. |
| 2 | Toggle the field off (deactivate). Confirm if prompted. | Success toast: "Custom field 'T-Shirt Size' has been deactivated." The field row now shows inactive status (e.g., greyed out or badge "Inactive"). |
| 3 | Navigate to the employee creation form. | The "Custom Fields" section does NOT contain "T-Shirt Size". |
| 4 | Navigate to Jane Smith's profile edit form. | The "Custom Fields" section does NOT show "T-Shirt Size". |
| 5 | Query Jane Smith via API: `GET /api/v1/tenant/employees/{jane.id}`. | The response still contains `custom_fields: {"tshirt_size": "L"}` in the JSONB column -- data is preserved. |
| 6 | Verify via DB or API that the custom field definition has `is_active = false`. | Confirmed. The definition still exists but is inactive. |

## 6. Postconditions
- The custom field is hidden from all forms and the directory.
- Existing JSONB data is intact on employee records.
- The field definition still exists in the configuration table with is_active = false.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
