---
id: TC-REC-ISO-007
user_story: US-REC-002
module: Recruitment
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-REC-ISO-007: Cross-tenant applicant writes are blocked; tenant_id and resume path are session-derived (no body-injected tenant_id / cross-tenant vacancy)

## 1. Test Objective
Verify AC-5 / NFR-3 / BR-3: the data layer prevents writing an applicant into another tenant's space. The `applicant.tenant_id` is always stamped from the resolved tenant context (not from the request body), an application cannot be created against a vacancy belonging to a different tenant, and the resume blob is written only under the resolving tenant's path. This exercises the EF Core global query filter (vacancy lookup) plus `TenantInterceptor` (write stamping). (Note: where US-REC-002 specifies RLS on applicant data, extend Step 5 to assert it at the DB session level.)

## 2. Related Requirements
- User Story: US-REC-002
- Acceptance Criteria: AC-5
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-2 (tenant-scoped resume path)
- Business Rules: BR-3 (sanitized, tenant-scoped storage key)

## 3. Preconditions
- Tenant "acme" and Tenant "globex" exist, each with at least one `Open` vacancy.
- A known Globex vacancy UUID is available for the cross-tenant attempt.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Resolving context | acme (subdomain/header) | Tenant doing the submit |
| Target vacancy | globex_vacancy_id | Tenant B vacancy |
| Injected body | "tenant_id": globex_id | Attempted override |
| Resume | valid.pdf | Allowed type |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Resolving as acme, `POST .../applications` against an acme vacancy with a body that injects `"tenant_id": globex_id` | The created applicant is stamped with acme's `tenant_id` (TenantInterceptor), NOT globex; the body value is ignored. |
| 2 | Resolving as acme, `POST .../vacancies/{globex_vacancy_id}/applications` (apply to a Globex vacancy) | 404/400 -- the Globex vacancy is hidden by the global query filter; no applicant is created in globex. |
| 3 | Inspect the resume blob from Step 1 | The object is stored under `{acme_tenantId}/recruitment/{vacancyId}/{applicantId}/...` -- never under the globex prefix; path is session-derived. |
| 4 | Switch to globex and verify | No acme-originated applicant exists in globex; globex's applicant set is unchanged. |
| 5 | Verify at the database level | The Step 1 applicant row has `tenant_id = acme`; no applicant row was created under globex from these attempts. (If an RLS policy exists on `applicant`, confirm an acme session cannot INSERT/UPDATE globex applicant rows even via direct SQL.) |

## 6. Postconditions
- No cross-tenant applicant write occurred; tenant_id and the resume storage path are session-derived, not client-controlled.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
