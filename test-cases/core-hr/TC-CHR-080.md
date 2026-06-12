---
id: TC-CHR-080
user_story: US-CHR-001
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-080: Soft delete -- employee records use is_deleted flag (BR-6)

## 1. Test Objective
Verify that employee records use soft delete (`is_deleted = true`) and are never hard-deleted via the UI. Soft-deleted employees should not appear in standard queries but should remain in the database.

## 2. Related Requirements
- User Story: US-CHR-001
- Business Rules: BR-6

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer or Tenant Admin role is authenticated in the "acme" tenant context.
- An employee "John Doe" exists in the tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Employee | John Doe | Existing employee to be deleted |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify "John Doe" appears in the employee list | Employee is visible. |
| 2 | Delete (or deactivate/archive) "John Doe" via the UI | Action completes successfully. |
| 3 | Verify "John Doe" no longer appears in the standard employee list | Employee is hidden from normal views. |
| 4 | Query the database directly: `SELECT is_deleted FROM employees WHERE first_name = 'John' AND last_name = 'Doe'` | `is_deleted = true`. The record still exists in the database. |
| 5 | Verify the EF Core global query filter excludes soft-deleted records | Standard API endpoints (`GET /api/v1/tenant/employees`) do not return the deleted employee. |
| 6 | Verify no hard-delete option is available in the UI | There is no "permanently delete" or "purge" button. |
| 7 | Verify an admin query (if available) with `IgnoreQueryFilters` can still retrieve the record | The record is accessible via admin/audit queries that bypass the soft-delete filter. |

## 6. Postconditions
- "John Doe" has `is_deleted = true` in the database.
- The record is hidden from standard queries but persists for audit and compliance purposes.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
