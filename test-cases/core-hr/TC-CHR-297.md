---
id: TC-CHR-297
user_story: US-CHR-012
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-297: Store and retrieve custom field value on employee JSONB column

## 1. Test Objective
Verify that when an HR Officer fills in a custom field value (e.g., "T-Shirt Size" = "L") during employee creation or profile edit, the value is persisted in the `custom_fields` JSONB column on the employee record. On subsequent retrieval (profile view or API GET), the stored value is correctly displayed and editable. This validates AC-3, FR-4, and FR-5.

## 2. Related Requirements
- User Story: US-CHR-012
- Acceptance Criteria: AC-3
- Functional Requirements: FR-4, FR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Custom field "T-Shirt Size" (dropdown, optional) exists for Employee entity.
- An HR Officer user is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee | Jane Smith (jane.smith@acme.test) | To be created or existing |
| T-Shirt Size Value | L | One of the valid dropdown options |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Add New Employee form. Fill in all mandatory fields and set "T-Shirt Size" to "L". | The dropdown shows "L" as selected. |
| 2 | Save the employee. | Success toast appears. Employee is created. |
| 3 | Query the employee via API: `GET /api/v1/tenant/employees/{id}`. | The response includes `custom_fields: {"tshirt_size": "L"}` in the JSONB payload. |
| 4 | Navigate to the newly created employee's profile. | The Custom Fields section displays "T-Shirt Size: L". |
| 5 | Click Edit on the Custom Fields section. Change "T-Shirt Size" to "XL". Save. | Success toast appears. |
| 6 | Reload the employee profile page. | "T-Shirt Size" now displays "XL". |
| 7 | Query via API again. | The response shows `custom_fields: {"tshirt_size": "XL"}`. |

## 6. Postconditions
- Employee record has `custom_fields` JSONB column containing `{"tshirt_size": "XL"}`.
- The value is retrievable and editable via both UI and API.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
