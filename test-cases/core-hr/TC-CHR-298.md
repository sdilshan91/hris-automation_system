---
id: TC-CHR-298
user_story: US-CHR-012
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-298: Usage count displayed on custom fields management page

## 1. Test Objective
Verify that the custom fields management page (Settings > Custom Fields) displays the current usage count for each custom field definition -- i.e., the number of employee records that have a value stored for that field in the JSONB column. This validates AC-1.

## 2. Related Requirements
- User Story: US-CHR-012
- Acceptance Criteria: AC-1

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Custom field "T-Shirt Size" (dropdown) exists for Employee entity.
- 3 employees in the tenant have a "T-Shirt Size" value set; 2 employees do not.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Custom Field | T-Shirt Size | Dropdown type |
| Employees with value | 3 | Jane (L), John (M), Sara (XL) |
| Employees without value | 2 | Mike, Lisa |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Settings > Custom Fields as Tenant Admin. | The management page loads with the list of custom fields. |
| 2 | Locate "T-Shirt Size" in the field list. | The row shows: Name "T-Shirt Size", Type "Dropdown", Required "No", Usage Count "3". |
| 3 | Create a new employee with "T-Shirt Size" = "S". | Employee created successfully. |
| 4 | Return to Settings > Custom Fields. | The usage count for "T-Shirt Size" is now "4". |
| 5 | Create a new custom field "Internal Notes" (text, optional). | The field is created with usage count "0". |

## 6. Postconditions
- Usage counts accurately reflect the number of employees with values for each custom field.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
