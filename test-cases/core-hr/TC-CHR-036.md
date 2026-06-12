---
id: TC-CHR-036
user_story: US-CHR-005
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-036: Create a new job title successfully (happy path)

## 1. Test Objective
Verify that a Tenant Admin can create a new job title with a unique name, that the record is persisted with `tenant_id` from session context, and that it appears in the job titles list. This validates the primary success flow of AC-2.

## 2. Related Requirements
- User Story: US-CHR-005
- Acceptance Criteria: AC-1, AC-2
- Functional Requirements: FR-1, FR-2
- Non-Functional Requirements: NFR-4
- Business Rules: BR-1, BR-4

## 3. Preconditions
- Tenant "acme" exists with status `active` and subdomain `acme.yourhrm.com`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- No job title named "Data Scientist" exists in the "acme" tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Authorized role |
| Title Name | Data Scientist | Required, unique within tenant |
| Grade | (none) | Optional, left empty |
| Description | Analyzes data and builds models | Optional |
| Status | Active | Default |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Job Titles management page at `https://acme.yourhrm.com/job-titles` | Job Titles list page loads. |
| 2 | Click the "Add Job Title" button (top-right) | A compact modal or slide-over panel appears with fields: Title Name (text input, required), Grade (searchable dropdown, optional), Description (textarea), Status toggle. |
| 3 | Enter "Data Scientist" in the Title Name field | Field accepts the input; no validation error displayed. |
| 4 | Leave Grade dropdown empty | Field shows placeholder (e.g., "Select Grade (optional)"). |
| 5 | Enter "Analyzes data and builds models" in the Description field | Field accepts the input. |
| 6 | Click "Save" / "Create" button | Loading indicator appears; button is disabled to prevent double-submit. |
| 7 | Observe API call `POST /api/v1/job-titles` with body `{ title_name: "Data Scientist", description: "Analyzes data and builds models" }` | Request is sent with `X-Tenant-Subdomain: acme` header. Response status is 201 Created. |
| 8 | Verify response body contains the new job title with `job_title_id` (UUID), `tenant_id` matching acme's tenant ID, `title_name: "Data Scientist"`, `is_active: true`, `grade_id: null`, `is_deleted: false` | All fields present and correct. |
| 9 | Verify the job title appears in the job titles list | "Data Scientist" row is visible with Title Name, Grade ("-"), Employee Count (0), Status (Active). |
| 10 | Verify an audit log entry exists for the create operation | Audit record contains `action: job_title_created`, `entity_id` matching the new job_title_id, `tenant_id`, `user_id`, and timestamp. |

## 6. Postconditions
- A new `job_title` record exists in the database with `tenant_id` set from session context.
- `is_active` is `true`, `is_deleted` is `false`.
- `created_at` and `created_by` are populated.
- An audit log entry of type `job_title_created` has been recorded.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
