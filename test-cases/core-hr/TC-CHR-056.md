---
id: TC-CHR-056
user_story: US-CHR-005
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-056: Audit log entries for job title create, update, and deactivate

## 1. Test Objective
Verify that audit log entries are created for all job title lifecycle operations (create, update, deactivate) as required by NFR-4. Each audit entry must include the action type, entity ID, tenant ID, user ID, timestamp, and changed fields (for updates).

## 2. Related Requirements
- User Story: US-CHR-005
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context (known `user_id`).
- No job title named "Audit Test Title" exists.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Authorized role |
| Title Name (create) | Audit Test Title | New title |
| Title Name (update) | Audit Test Title Updated | Renamed |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create a new job title "Audit Test Title" via `POST /api/v1/job-titles` | Response status is 201 Created. Note the returned `job_title_id`. |
| 2 | Query the audit log for the create event (via audit API or direct DB query) | An audit entry exists with: `action: job_title_created`, `entity_id` matching the new job_title_id, `tenant_id` matching acme's tenant, `user_id` matching the authenticated user, and a recent timestamp. |
| 3 | Update the job title name to "Audit Test Title Updated" via `PUT /api/v1/job-titles/{id}` | Response status is 200 OK. |
| 4 | Query the audit log for the update event | An audit entry exists with: `action: job_title_updated`, `entity_id`, `tenant_id`, `user_id`, timestamp, and a record of changed fields (e.g., `title_name` from "Audit Test Title" to "Audit Test Title Updated"). |
| 5 | Deactivate the job title via `PATCH /api/v1/job-titles/{id}/deactivate` | Response status is 200 OK. |
| 6 | Query the audit log for the deactivation event | An audit entry exists with: `action: job_title_deactivated`, `entity_id`, `tenant_id`, `user_id`, and timestamp. |
| 7 | Verify the total number of audit entries for this entity is 3 (create, update, deactivate) | Three distinct audit log entries exist for the job_title_id, in chronological order. |

## 6. Postconditions
- Three audit log entries exist for the job title: created, updated, deactivated.
- Each entry is scoped to the correct tenant and user.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
