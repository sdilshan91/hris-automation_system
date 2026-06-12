---
id: TC-CHR-082
user_story: US-CHR-001
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-082: Optional user_id linking for self-service portal (FR-8)

## 1. Test Objective
Verify that an employee record can optionally be linked to a global user account (`user_id` FK) for self-service portal access, and that employees without a linked user account are still valid records.

## 2. Related Requirements
- User Story: US-CHR-001
- Functional Requirements: FR-8

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- A user account "jdoe@acme.com" exists in the system (not yet linked to any employee).
- Department and job title exist in the tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Employee 1 | John Doe | To be linked to user account |
| User account | jdoe@acme.com (user_id: UUID) | Existing user |
| Employee 2 | Jane Smith | No user account linked |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create employee "John Doe" and link user_id to "jdoe@acme.com" | Employee created with `user_id` populated. |
| 2 | Verify the database shows user_id FK is set for "John Doe" | `user_id` column has the UUID of jdoe@acme.com. |
| 3 | Create employee "Jane Smith" without linking any user account | Employee created with `user_id` = null. |
| 4 | Verify "Jane Smith" exists as a valid employee without a user link | Record is valid; `user_id` is null. |
| 5 | Verify both employees appear in the employee list | Both are listed regardless of user_id status. |

## 6. Postconditions
- "John Doe" has a user_id link; "Jane Smith" does not.
- Both are valid employee records.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
