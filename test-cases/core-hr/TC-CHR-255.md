---
id: TC-CHR-255
user_story: US-CHR-010
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-255: Default status is 'active' when no status column provided in import file

## 1. Test Objective
Verify that imported employees default to `active` status when the import file does not include a `status` column, per BR-4. When a `status` column IS provided with valid values, those values are respected.

## 2. Related Requirements
- User Story: US-CHR-010
- Business Rules: BR-4
- Functional Requirements: FR-3

## 3. Preconditions
- Tenant "acme" exists with status `active` and sufficient capacity.
- An HR Officer user is authenticated in the "acme" tenant context.
- Required departments and job titles exist.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona |
| File 1 | no_status_column.csv | 3 rows, no `status` column at all |
| File 2 | with_status_column.csv | 3 rows, `status` column with values: "active", "probation", "" (empty) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Upload `no_status_column.csv` (no status column in headers) and click "Import". | 3 employees created. |
| 2 | Query the status of the 3 new employees. | All 3 have `status = 'active'` (default per BR-4). |
| 3 | Upload `with_status_column.csv` and click "Import". | 3 employees created. |
| 4 | Query the status of the 3 new employees. | Row with "active" has `status = 'active'`. Row with "probation" has `status = 'probation'`. Row with empty status has `status = 'active'` (default). |

## 6. Postconditions
- All imported employees have correct status values, with `active` as the default.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
