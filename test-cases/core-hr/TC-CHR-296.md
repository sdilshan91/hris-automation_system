---
id: TC-CHR-296
user_story: US-CHR-012
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-296: Custom field dynamically rendered on employee create and profile edit forms

## 1. Test Objective
Verify that after a custom field definition is created, it dynamically renders on the employee creation form (US-CHR-001) and employee profile edit form (US-CHR-002) within the "Custom Fields" / "Additional Information" section. The rendering must match the field type (dropdown shows a select, text shows an input, etc.) and respect the display order. This validates AC-2 and FR-9.

## 2. Related Requirements
- User Story: US-CHR-012
- Acceptance Criteria: AC-2
- Functional Requirements: FR-9
- Non-Functional Requirements: NFR-6

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A Tenant Admin has created custom fields: "T-Shirt Size" (dropdown, optional, order 1) and "Project Code" (text, required, order 2).
- An HR Officer user is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Custom Field 1 | T-Shirt Size (dropdown, optional, order 1) | Options: S, M, L, XL |
| Custom Field 2 | Project Code (text, required, order 2) | Short text field |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Add New Employee form. | The form includes a "Custom Fields" or "Additional Information" section at the appropriate step. |
| 2 | Inspect the Custom Fields section. | "T-Shirt Size" renders as a dropdown/select element with options S, M, L, XL. "Project Code" renders as a text input with a required indicator (asterisk or similar). |
| 3 | Verify field ordering. | "T-Shirt Size" (order 1) appears before "Project Code" (order 2). |
| 4 | Navigate to an existing employee's profile > Edit. | The same Custom Fields section appears in the profile edit form with identical field types and ordering. |
| 5 | Verify required indicator on "Project Code". | The field shows a required indicator matching other required fields in the form. |

## 6. Postconditions
- Custom fields render correctly on both forms with proper types and order.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
