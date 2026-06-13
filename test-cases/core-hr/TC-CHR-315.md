---
id: TC-CHR-315
user_story: US-CHR-012
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-13
---

# TC-CHR-315: Custom field columns in directory export and bulk import (DEFERRED)

## 1. Test Objective
Verify that custom field values are included as additional columns in the employee directory export (US-CHR-003, CSV/Excel) and that the bulk import template (US-CHR-010) includes custom field columns which are correctly mapped and stored during import. This validates FR-10.

**STATUS: DEFERRED** -- The bulk import implementation (US-CHR-010) explicitly defers custom field column mapping (FR-11 of US-CHR-010). The directory export integration with custom fields is also pending. This test case is defined for execution once the export/import flows are updated to include custom field columns.

## 2. Related Requirements
- User Story: US-CHR-012
- Functional Requirements: FR-10
- Dependencies: US-CHR-003 (export), US-CHR-010 (import)

## 3. Preconditions
- Tenant "acme" exists with custom fields "T-Shirt Size" (dropdown) and "Project Code" (text).
- Employees exist with custom field values.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Custom Field 1 | T-Shirt Size | Dropdown |
| Custom Field 2 | Project Code | Text |
| Employees | 5 with values | Various custom field values |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Export employee directory as CSV. | The CSV includes columns "T-Shirt Size" and "Project Code" with correct values per employee. |
| 2 | Export employee directory as Excel. | The Excel file includes the same custom field columns. |
| 3 | Download the bulk import template. | The template includes custom field columns (T-Shirt Size, Project Code) with appropriate headers and sample values. |
| 4 | Upload an import file with custom field columns filled in. | The imported employees have correct custom_fields JSONB values stored. |
| 5 | Verify a row with an invalid dropdown value (e.g., "XXS" for T-Shirt Size). | The row is flagged as invalid with a validation error for the custom field. |

## 6. Postconditions
- Custom field data round-trips through export and import correctly.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
