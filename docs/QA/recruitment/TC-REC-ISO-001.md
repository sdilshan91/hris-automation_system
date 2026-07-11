---
id: TC-REC-ISO-001
user_story: US-REC-001
module: Recruitment
priority: critical
type: security
status: pass
created: 2026-06-15
---

# TC-REC-ISO-001: Recruiter in Tenant A cannot see or retrieve Tenant B's vacancies (cross-tenant read isolation)

## 1. Test Objective
Verify AC-4: vacancy data is fully tenant-isolated on reads -- a user authenticated in Tenant A cannot list or retrieve any `vacancy` belonging to Tenant B. This exercises EF Core global query filters (the codebase's tenant-isolation mechanism). (Note: US-REC-001 AC-4/NFR-2 specify a PostgreSQL RLS policy on the `vacancy` table; this platform currently enforces isolation via EF Core global query filters + TenantInterceptor. If an RLS policy is later added on `vacancy`, extend Step 5 to assert it at the DB session level as defense-in-depth.)

## 2. Related Requirements
- User Story: US-REC-001
- Acceptance Criteria: AC-4
- Non-Functional Requirements: NFR-2
- Data Requirements: S7 (tenant_id discriminator + RLS policy)

## 3. Preconditions
- Tenant "acme" has vacancies (incl. "Acme Role", `Open`).
- Tenant "globex" has vacancies (incl. "Globex Role", `Open`).
- A recruiter with `Recruitment.Read.All` is authenticated in acme.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Has "Acme Role" |
| Tenant B | globex | Has "Globex Role" |
| Auth context | acme | Recruiter authenticated in Tenant A |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate in acme; JWT carries acme's `tenant_id` | Tenant context resolves to acme. |
| 2 | `GET /api/v1/recruitment/vacancies` | Response contains only acme vacancies. Zero globex vacancies (AC-4: Tenant B sees zero of Tenant A's, and vice versa). |
| 3 | `GET /api/v1/recruitment/vacancies/{globex_vacancy_id}` using a Globex vacancy UUID | 404 Not Found (EF global query filter excludes it); never 200 with another tenant's data. |
| 4 | Switch to globex context and repeat the list/fetch | globex sees only its own vacancies; zero acme vacancies. |
| 5 | Verify at the database level | `SELECT * FROM vacancy WHERE tenant_id = acme_id` returns only acme rows; `... = globex_id` returns only globex rows. (If an RLS policy exists, confirm a session set to acme cannot read globex rows even via a direct query.) |

## 6. Postconditions
- No cross-tenant vacancy data was exposed via API or query.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
