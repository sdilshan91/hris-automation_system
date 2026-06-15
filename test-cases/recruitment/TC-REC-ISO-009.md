---
id: TC-REC-ISO-009
user_story: US-REC-003
module: Recruitment
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-REC-ISO-009: A recruiter in Tenant B sees zero of Tenant A's pipeline (cross-tenant pipeline read isolation)

## 1. Test Objective
Verify AC-5 / NFR-3: the applicant pipeline is fully tenant-isolated on reads -- a recruiter authenticated in Tenant B cannot view, list, or retrieve any of Tenant A's pipeline board data, applicant cards, applicant detail, or stage history. This exercises EF Core global query filters on the `applicant` and `applicant_stage_history` tables (the codebase's tenant-isolation mechanism). (Note: US-REC-003 AC-5/NFR-3 specify PostgreSQL RLS as defense-in-depth on applicant data; this platform currently enforces isolation via EF Core global query filters + TenantInterceptor. If RLS is later added, extend Step 5 to assert it at the DB session level.)

## 2. Related Requirements
- User Story: US-REC-003
- Acceptance Criteria: AC-5
- Non-Functional Requirements: NFR-3
- Data Requirements: S7 (`applicant`, `applicant_stage_history`, tenant_id discriminator)

## 3. Preconditions
- Tenant "acme" (Tenant A) has a vacancy "Senior Backend Engineer" with applicants across stages and stage history.
- Tenant "globex" (Tenant B) has its own vacancies/applicants.
- A recruiter with `Recruitment.Read.All` is authenticated in globex.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Has the target pipeline |
| Tenant B | globex | Auth context |
| acme vacancy id | {acme_vacancyId} | Used in cross-tenant attempts |
| acme applicant id | {acme_applicantId} | Used in cross-tenant attempts |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate in globex; JWT carries globex's tenant_id | Tenant context resolves to globex. |
| 2 | `GET /api/v1/recruitment/vacancies/{acme_vacancyId}/pipeline` | 404 Not Found (the vacancy + its applicants are filtered out under globex); never 200 with acme's board (AC-5). |
| 3 | `GET /api/v1/recruitment/applications/{acme_applicantId}` | 404 Not Found (EF global query filter excludes it); never 200 with acme's applicant. |
| 4 | Request acme's applicant stage history / timeline as globex | Denied (404); no cross-tenant `applicant_stage_history` rows returned. |
| 5 | Verify at the database level | `SELECT * FROM applicant WHERE tenant_id = globex_id` returns only globex rows; acme applicant + history rows are not visible under globex context. (If RLS exists, confirm a globex session cannot read acme rows even via direct SQL.) |

## 6. Postconditions
- No cross-tenant pipeline, applicant, or stage-history data was exposed via API or query.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
