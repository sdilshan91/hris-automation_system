---
id: TC-CHR-253
user_story: US-CHR-010
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-253: Role-based access -- only HR Officer and Tenant Admin can perform bulk import

## 1. Test Objective
Verify that only users with the HR Officer or Tenant Admin role can access the bulk import endpoint and perform imports. Users with Manager or Employee roles should receive a 403 Forbidden response. This validates role-based authorization for the import feature.

## 2. Related Requirements
- User Story: US-CHR-010
- Preconditions (Section 2): HR Officer or Tenant Admin role required
- Business Rules: BR-1 (tenant_id from session implies authorized session)

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Users exist with different roles: HR Officer, Tenant Admin, Manager, Employee.
- A valid import file `valid_import.csv` with 3 rows is available.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| HR Officer | hr@acme.test | Should be allowed |
| Tenant Admin | admin@acme.test | Should be allowed |
| Manager | mgr@acme.test | Should be denied |
| Employee | emp@acme.test | Should be denied |
| File | valid_import.csv | 3 valid rows |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Manager (`mgr@acme.test`). Send POST to the bulk import endpoint with `valid_import.csv`. | Response: 403 Forbidden. No employees created. |
| 2 | Authenticate as Employee (`emp@acme.test`). Send POST to the bulk import endpoint with `valid_import.csv`. | Response: 403 Forbidden. No employees created. |
| 3 | Authenticate as HR Officer (`hr@acme.test`). Send POST to the bulk import endpoint with `valid_import.csv`. | Response: 200 OK. 3 employees created successfully. |
| 4 | Authenticate as Tenant Admin (`admin@acme.test`). Send POST to the bulk import endpoint with `valid_import.csv` (different emails). | Response: 200 OK. 3 employees created successfully. |
| 5 | Verify the UI: navigate to the import page as Manager. | The import page is either not accessible (redirect or 403) or the Import button is disabled/hidden. |

## 6. Postconditions
- Only HR Officer and Tenant Admin imports succeeded.
- Manager and Employee attempts were rejected.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
