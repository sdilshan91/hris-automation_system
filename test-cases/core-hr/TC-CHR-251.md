---
id: TC-CHR-251
user_story: US-CHR-010
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-251: Audit log records import operation with file name, row count, success count, and failure count

## 1. Test Objective
Verify that every bulk import operation is logged in the audit trail with the file name, total row count, success count, and failure count, along with the actor who performed the import. This validates FR-10.

## 2. Related Requirements
- User Story: US-CHR-010
- Functional Requirements: FR-10

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user (`hr-officer-uuid`) is authenticated in the "acme" tenant context.
- Import file `audit_test.csv` has 10 rows: 8 valid, 2 invalid.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Actor performing import |
| File Name | audit_test.csv | 10 rows (8 valid, 2 invalid) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Upload `audit_test.csv` and click "Import". | System processes the file: 8 imported, 2 failed. |
| 2 | Query the audit log table for the most recent "bulk_employee_import" event for tenant "acme". | An audit entry exists with: `action` = "bulk_employee_import", `file_name` = "audit_test.csv", `total_rows` = 10, `success_count` = 8, `failure_count` = 2, `performed_by` = hr-officer-uuid, `tenant_id` = acme tenant UUID, `created_at` ~ now(). |
| 3 | Perform a second import with a fully valid 3-row file. | A second audit entry is created with `total_rows` = 3, `success_count` = 3, `failure_count` = 0. |
| 4 | Verify both audit entries are scoped to tenant "acme". | Both have `tenant_id` = acme tenant UUID. |

## 6. Postconditions
- Audit log contains entries for all import operations with accurate counts.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
