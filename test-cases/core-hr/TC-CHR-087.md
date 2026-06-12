---
id: TC-CHR-087
user_story: US-CHR-001
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-087: Department_id and job_title_id must exist in tenant

## 1. Test Objective
Verify that the system rejects employee creation if the supplied department_id or job_title_id does not exist in the current tenant. Also verify that a department_id or job_title_id from another tenant is rejected (cross-tenant reference prevention).

## 2. Related Requirements
- User Story: US-CHR-001
- Data Requirements: department_id "Must exist in tenant", job_title_id "Must exist in tenant"
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with department "Engineering" and job title "Software Engineer".
- Tenant "globex" exists with department "Marketing" (department ID known).
- A user with HR Officer role is authenticated in "acme".

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Invalid department_id | random-non-existent-uuid | Does not exist in any tenant |
| Cross-tenant department_id | globex-marketing-uuid | Exists in Tenant B, not Tenant A |
| Valid department_id | acme-engineering-uuid | Exists in Tenant A |
| Invalid job_title_id | random-non-existent-uuid | Does not exist |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send POST request with department_id = random-non-existent-uuid | 400/422 error: "Department not found." |
| 2 | Send POST request with department_id = globex-marketing-uuid (Tenant B's department) | 400/422 error: "Department not found." The EF global query filter scoped to Tenant A prevents resolving Tenant B's department. |
| 3 | Send POST request with job_title_id = random-non-existent-uuid | 400/422 error: "Job title not found." |
| 4 | Send POST request with valid department_id and job_title_id from Tenant A | Employee created successfully (201 Created). |

## 6. Postconditions
- Only employees referencing valid, same-tenant department and job title records can be created.
- Cross-tenant FK references are impossible.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
