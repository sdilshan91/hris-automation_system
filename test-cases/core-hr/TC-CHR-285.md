---
id: TC-CHR-285
user_story: US-CHR-011
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-285: Only HR Officer and Tenant Admin can assign reporting managers

## 1. Test Objective
Verify that manager assignment operations are restricted to users with HR Officer or Tenant Admin roles. Users with Manager or Employee roles attempting to assign reporting managers receive HTTP 403 Forbidden. This validates the role-based access control requirement from Section 2.

## 2. Related Requirements
- User Story: US-CHR-011
- Preconditions: Section 2 ("HR Officer or Tenant Admin role")

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Users with HR Officer, Tenant Admin, Manager, and Employee roles exist.
- Employee E and Manager M exist.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| HR Officer | hr@acme.test | Authorized |
| Tenant Admin | admin@acme.test | Authorized |
| Manager User | mgr.user@acme.test | NOT authorized to assign |
| Employee User | emp.user@acme.test | NOT authorized to assign |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as HR Officer. Assign Manager M to Employee E. | 200 OK. Assignment succeeds. |
| 2 | Revert the assignment (set back to null or original). | 200 OK. |
| 3 | Authenticate as Tenant Admin. Assign Manager M to Employee E. | 200 OK. Assignment succeeds. |
| 4 | Revert the assignment. | 200 OK. |
| 5 | Authenticate as Manager User. Attempt to assign Manager M to Employee E via API. | HTTP 403 Forbidden. |
| 6 | Authenticate as Employee User. Attempt to assign Manager M to Employee E via API. | HTTP 403 Forbidden. |
| 7 | Verify Employee E's record is unchanged after steps 5-6. | `reports_to_employee_id` is not modified by unauthorized users. |

## 6. Postconditions
- Employee E's manager is unchanged from the final authorized revert.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
