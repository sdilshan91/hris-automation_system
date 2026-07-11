---
id: TC-REC-ISO-003
user_story: US-REC-001
module: Recruitment
priority: critical
type: security
status: pass
created: 2026-06-15
---

# TC-REC-ISO-003: Cross-tenant vacancy writes are blocked at the data layer (no body-injected tenant_id, no cross-tenant edit/publish/close)

## 1. Test Objective
Verify that the data layer prevents a Tenant A user from writing into Tenant B's data: `tenant_id` is always stamped from the resolved session context (not the request body), and a Tenant A user cannot create, edit, publish, or close a vacancy that resolves to Tenant B. This exercises the EF Core global query filter (reads) plus the `TenantInterceptor` (writes). (Note: where US-REC-001 specifies an RLS policy on `vacancy`, extend Step 5 to assert it at the DB session level.)

## 2. Related Requirements
- User Story: US-REC-001
- Acceptance Criteria: AC-4
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-2 (status mutations), FR-7 (audit)
- Test Hint: create vacancies in two tenants, confirm cross-tenant queries/mutations are blocked

## 3. Preconditions
- Tenant "acme" and Tenant "globex" exist, each with at least one vacancy.
- A recruiter with `Recruitment.Create.All` is authenticated in acme.
- A known Globex vacancy UUID is available for the cross-tenant attempts.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Auth context | acme | Authorized creator in Tenant A |
| Target | globex_vacancy_id | Tenant B vacancy |
| Injected body | "tenant_id": globex_id | Attempted override |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As an acme recruiter, `POST /api/v1/recruitment/vacancies` with a body that injects `"tenant_id": globex_id` | The created vacancy is stamped with acme's `tenant_id` (TenantInterceptor), NOT globex; the body value is ignored. |
| 2 | As an acme recruiter, `PUT /api/v1/recruitment/vacancies/{globex_vacancy_id}` to edit a Globex vacancy | 404 Not Found (the global query filter hides it); no Globex row is modified. |
| 3 | As an acme recruiter, `POST .../{globex_vacancy_id}/publish` and `.../close` | 404 Not Found; no Globex status change; no audit entry written into globex. |
| 4 | Switch to globex and verify the targeted vacancy is unchanged | Globex vacancy retains its original status/fields; no acme-originated mutation occurred. |
| 5 | Verify at the database level | The Globex vacancy row is unchanged; the acme-created row from Step 1 has `tenant_id = acme`. (If an RLS policy exists on `vacancy`, confirm an acme session cannot UPDATE globex rows even via direct SQL.) |

## 6. Postconditions
- No cross-tenant vacancy write occurred; tenant_id is session-derived, not client-controlled.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
