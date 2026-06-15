---
id: TC-REC-ISO-005
user_story: US-REC-002
module: Recruitment
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-REC-ISO-005: A user in Tenant B sees zero of Tenant A's applicants (cross-tenant applicant read isolation)

## 1. Test Objective
Verify AC-5 / NFR-3: applicant data is fully tenant-isolated on reads -- a recruiter authenticated in Tenant B cannot list or retrieve any `applicant` (or its resume) belonging to Tenant A. This exercises EF Core global query filters on the `applicant` table (the codebase's tenant-isolation mechanism). (Note: US-REC-002 AC-5/NFR-3 specify PostgreSQL RLS policies on applicant data; this platform currently enforces isolation via EF Core global query filters + TenantInterceptor. If an RLS policy is later added on `applicant`, extend Step 5 to assert it at the DB session level as defense-in-depth.)

## 2. Related Requirements
- User Story: US-REC-002
- Acceptance Criteria: AC-5
- Non-Functional Requirements: NFR-3
- Data Requirements: S7 (`applicant` table, tenant_id discriminator + RLS policy)

## 3. Preconditions
- Tenant "acme" (Tenant A) has applicants (e.g. jordan.rivera@example.com applied to "Senior Backend Engineer").
- Tenant "globex" (Tenant B) has its own applicants.
- A recruiter with `Recruitment.Read.All` is authenticated in globex.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Has applicant "Jordan Rivera" |
| Tenant B | globex | Has its own applicants |
| Auth context | globex | Recruiter authenticated in Tenant B |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate in globex; JWT carries globex's `tenant_id` | Tenant context resolves to globex. |
| 2 | `GET /api/v1/recruitment/vacancies/{anyVacancy}/applications` and the tenant-wide applicant list | Response contains only globex applicants; zero acme applicants (AC-5: Tenant B sees zero of Tenant A's). |
| 3 | `GET /api/v1/recruitment/applications/{acme_applicant_id}` using an acme applicant UUID | 404 Not Found (EF global query filter excludes it); never 200 with another tenant's applicant. |
| 4 | Attempt to download the acme applicant's resume via the API as globex | Denied (404/403); no cross-tenant resume retrieval. |
| 5 | Verify at the database level | `SELECT * FROM applicant WHERE tenant_id = globex_id` returns only globex rows; acme applicant rows are not visible under globex context. (If an RLS policy exists, confirm a session set to globex cannot read acme applicant rows even via direct SQL.) |

## 6. Postconditions
- No cross-tenant applicant data or resume was exposed via API or query.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
