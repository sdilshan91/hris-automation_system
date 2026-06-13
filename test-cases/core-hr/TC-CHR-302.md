---
id: TC-CHR-302
user_story: US-CHR-012
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-302: Required custom field missing on employee save -- validation error

## 1. Test Objective
Verify that when a custom field is marked as required (is_required = true) and a user attempts to create or save an employee without providing a value for that field, the system rejects the submission with a validation error. This validates FR-5.

## 2. Related Requirements
- User Story: US-CHR-012
- Functional Requirements: FR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Custom field "Project Code" of type "Text" exists for Employee entity, marked as required.
- An HR Officer is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Custom Field | Project Code | Type: text, is_required: true |
| Employee | New employee being created | All mandatory base fields filled |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Add New Employee form. Fill in all mandatory base fields (name, email, etc.). | The form shows a "Custom Fields" section with "Project Code" marked as required (asterisk). |
| 2 | Leave "Project Code" empty. Attempt to save. | Client-side validation error: "Project Code is required" (or similar). The form does not submit. |
| 3 | Attempt via API: `POST /api/v1/tenant/employees` with all base fields but `custom_fields: {}` (missing project_code). | API returns HTTP 400/422 with validation error: "Project Code is required." |
| 4 | Fill in "Project Code" with "PRJ-42". Save. | Employee is created successfully. `custom_fields: {"project_code": "PRJ-42"}` is stored. |

## 6. Postconditions
- Employees cannot be created or updated without providing values for required custom fields.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
