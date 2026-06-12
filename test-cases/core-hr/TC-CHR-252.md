---
id: TC-CHR-252
user_story: US-CHR-010
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-252: tenant_id always set from session context -- file content cannot override tenant assignment

## 1. Test Objective
Verify that the `tenant_id` on all imported employee records is always set from the authenticated session's tenant context and never from the import file, even if the file contains a `tenant_id` column with a different tenant's UUID. This validates BR-1 and FR-6, a critical security requirement.

## 2. Related Requirements
- User Story: US-CHR-010
- Acceptance Criteria: AC-2
- Functional Requirements: FR-6
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" (tenant UUID: `acme-uuid`) exists with status `active`.
- Tenant "globex" (tenant UUID: `globex-uuid`) also exists.
- An HR Officer user is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant -- session context = acme |
| User Role | HR Officer | Authenticated in acme |
| File Name | tenant_override_attempt.csv | Contains a `tenant_id` column with `globex-uuid` |
| Row 1 | Jane,Doe,jane@acme.test,...,Engineering,Software Engineer,Full-Time,globex-uuid | Malicious tenant_id column |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Upload `tenant_override_attempt.csv` (which includes a `tenant_id` column with `globex-uuid` values) and click "Import". | System processes the file. The `tenant_id` column in the file is either ignored or treated as an unrecognized custom column. |
| 2 | Wait for processing to complete. | Import succeeds for valid rows. |
| 3 | Query the `employees` table for the newly imported records. | All imported employees have `tenant_id` = `acme-uuid` (the session tenant). None have `tenant_id` = `globex-uuid`. |
| 4 | Verify from tenant "globex" context. | The imported employees are NOT visible when querying from the "globex" tenant context. |

## 6. Postconditions
- All imported employees belong to the "acme" tenant regardless of file content.
- No cross-tenant data pollution occurred.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
