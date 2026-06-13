---
id: TC-CHR-305
user_story: US-CHR-012
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-305: Reactivate custom field restores visibility with previously stored values intact

## 1. Test Objective
Verify that reactivating a previously deactivated custom field restores its visibility on employee forms and the directory, and that the values previously stored in the JSONB column are displayed correctly. This validates AC-5, FR-7, and BR-3.

## 2. Related Requirements
- User Story: US-CHR-012
- Acceptance Criteria: AC-5
- Functional Requirements: FR-7
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Custom field "T-Shirt Size" was previously active, data was stored, and then deactivated (as per TC-CHR-304).
- Employee "Jane Smith" has `custom_fields: {"tshirt_size": "L"}` (preserved from before deactivation).
- Tenant Admin is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Custom Field | T-Shirt Size | Currently deactivated |
| Employee | Jane Smith | JSONB still has tshirt_size: "L" |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Settings > Custom Fields. Locate "T-Shirt Size" (shown as inactive). | The field is visible in the management list with inactive status. |
| 2 | Toggle the field on (reactivate). | Success toast: "Custom field 'T-Shirt Size' has been reactivated." The field row now shows active status. |
| 3 | Navigate to the employee creation form. | The "Custom Fields" section again contains "T-Shirt Size" as a dropdown. |
| 4 | Navigate to Jane Smith's profile. | The Custom Fields section shows "T-Shirt Size: L" -- the value preserved from before deactivation. |
| 5 | Navigate to Jane Smith's profile edit form. | "T-Shirt Size" is editable and pre-filled with "L". |

## 6. Postconditions
- The custom field is active again and visible on all forms.
- Previously stored values are displayed correctly without data loss.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
