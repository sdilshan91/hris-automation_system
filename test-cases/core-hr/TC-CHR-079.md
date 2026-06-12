---
id: TC-CHR-079
user_story: US-CHR-001
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-079: Default status "active" and explicit "probation" (BR-3)

## 1. Test Objective
Verify that when no status is explicitly set during employee creation, the default is "active" (BR-3). Also verify that status can be explicitly set to "probation" on creation.

## 2. Related Requirements
- User Story: US-CHR-001
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- Department and job title exist in the tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Employee 1 | John Doe | Status field left empty/default |
| Employee 2 | Jane Smith | Status explicitly set to "probation" |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create employee "John Doe" with all mandatory fields, leaving the status field unset/default | Employee created successfully. |
| 2 | Verify the employee's status in the database and API response is "active" | Status = "active" (BR-3 default). |
| 3 | Create employee "Jane Smith" with all mandatory fields, explicitly setting status = "probation" | Employee created successfully. |
| 4 | Verify the employee's status in the database and API response is "probation" | Status = "probation". |
| 5 | Verify both employees appear in the employee list with their respective statuses | "John Doe" shows "active", "Jane Smith" shows "probation". |

## 6. Postconditions
- "John Doe" has status "active" (default).
- "Jane Smith" has status "probation" (explicit).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
