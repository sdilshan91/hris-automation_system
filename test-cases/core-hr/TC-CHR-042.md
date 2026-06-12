---
id: TC-CHR-042
user_story: US-CHR-005
module: Core HR
priority: critical
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-042: Same job title name allowed in different tenants

## 1. Test Objective
Verify that two different tenants can each have a job title with the same name, confirming that the uniqueness constraint on `title_name` is scoped to the tenant level (AC-3, BR-1). This also validates cross-tenant data isolation at the application layer.

## 2. Related Requirements
- User Story: US-CHR-005
- Acceptance Criteria: AC-3
- Functional Requirements: FR-2
- Non-Functional Requirements: NFR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active` and subdomain `acme.yourhrm.com`.
- Tenant "globex" exists with status `active` and subdomain `globex.yourhrm.com`.
- A user with Tenant Admin role exists in both tenants.
- A job title named "Product Manager" exists in the "acme" tenant.
- No job title named "Product Manager" exists in the "globex" tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A Subdomain | acme.yourhrm.com | Has "Product Manager" title |
| Tenant B Subdomain | globex.yourhrm.com | Does not have "Product Manager" |
| Title Name | Product Manager | Same name in both tenants |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate as Tenant Admin in the "acme" tenant | User is in acme context. |
| 2 | Verify `GET /api/v1/job-titles` returns "Product Manager" in the list | "Product Manager" exists in acme. |
| 3 | Switch to (or authenticate as) Tenant Admin in the "globex" tenant | User is now in globex context. |
| 4 | Navigate to Job Titles management page for globex | Page loads; "Product Manager" is NOT in the list. |
| 5 | Click "Add Job Title" and enter "Product Manager" in the Title Name field | Field accepts the input. |
| 6 | Click "Save" | Request is submitted with globex tenant context. |
| 7 | Observe API response | Response status is 201 Created. The job title is created in the globex tenant. |
| 8 | Verify both tenants now have a "Product Manager" title with different `job_title_id` and different `tenant_id` values | Each record has its own UUID and is scoped to its respective tenant. |
| 9 | Switch back to acme and verify acme's "Product Manager" is unchanged | Acme's original record is unaffected. |

## 6. Postconditions
- Both "acme" and "globex" tenants have a "Product Manager" job title, each with a distinct `job_title_id` and their own `tenant_id`.
- No cross-tenant data leakage occurred.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
