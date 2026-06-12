---
id: TC-CHR-267
user_story: US-CHR-010
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-267: Custom field columns in import file -- DEFERRED to US-CHR-012

## 1. Test Objective
Verify that import files containing `custom_field_*` columns are mapped to the tenant's configured custom fields and stored in the employee's JSONB custom_fields column. This test is DEFERRED pending US-CHR-012 (Custom Fields Configuration), which defines the tenant custom field schema.

## 2. Related Requirements
- User Story: US-CHR-010
- Functional Requirements: FR-11
- Dependencies: US-CHR-012 (Custom Fields Configuration)

## 3. Preconditions
- DEFERRED: Requires US-CHR-012 to define tenant custom field schemas.
- When US-CHR-012 is delivered: tenant "acme" has custom fields configured (e.g., "custom_field_shirt_size", "custom_field_parking_spot").

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| DEFERRED | Pending US-CHR-012 | Custom field column mapping |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | (DEFERRED) Upload a CSV with `custom_field_shirt_size` and `custom_field_parking_spot` columns alongside standard columns. | Custom field values are validated against the tenant's custom field schema. |
| 2 | (DEFERRED) Verify the imported employee records. | `custom_fields` JSONB column contains `{ "shirt_size": "L", "parking_spot": "B-42" }`. |
| 3 | (DEFERRED) Upload with an unrecognized custom field column (e.g., `custom_field_nonexistent`). | The unrecognized column is either ignored or reported as a warning. |

## 6. Postconditions
- DEFERRED until US-CHR-012 delivers custom field configuration.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
