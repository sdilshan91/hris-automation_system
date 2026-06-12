---
id: TC-CHR-081
user_story: US-CHR-001
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-081: Audit columns auto-populated on employee creation (FR-7)

## 1. Test Objective
Verify that the audit columns (`created_at`, `created_by`, `updated_at`, `updated_by`) are automatically populated when an employee record is created, and that `updated_at`/`updated_by` are updated on subsequent modifications.

## 2. Related Requirements
- User Story: US-CHR-001
- Functional Requirements: FR-7

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user "hruser@acme.com" with HR Officer role is authenticated in the "acme" tenant context.
- Department and job title exist in the tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Authenticated user | hruser@acme.com | HR Officer |
| Employee | John Doe | New employee |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create employee "John Doe" with all mandatory fields | Employee created successfully (201 Created). |
| 2 | Query the database for the new employee's audit columns | `created_at` is set to the current timestamp (within seconds of creation). `created_by` is set to the authenticated user's ID (hruser@acme.com's user ID). `updated_at` equals `created_at`. `updated_by` equals `created_by`. |
| 3 | Wait a few seconds, then update the employee (e.g., change last_name) | Update succeeds (200 OK). |
| 4 | Query the database again | `updated_at` is now later than `created_at`. `updated_by` reflects the user who performed the update. `created_at` and `created_by` remain unchanged. |

## 6. Postconditions
- Audit columns are correctly populated and maintained through the entity lifecycle.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
