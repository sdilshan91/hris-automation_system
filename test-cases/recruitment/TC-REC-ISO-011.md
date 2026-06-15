---
id: TC-REC-ISO-011
user_story: US-REC-003
module: Recruitment
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-REC-ISO-011: Cross-tenant stage moves blocked; tenant_id + stage-history rows session-derived (write isolation)

## 1. Test Objective
Verify AC-5 / NFR-3 / BR-5: a user in Tenant B cannot move, advance, reject, or bulk-update an applicant belonging to Tenant A, even when supplying Tenant A's applicant id or a body-injected `tenant_id`. Any stage-history row written is stamped with the session-derived tenant_id (via TenantInterceptor), never a client-supplied value.

## 2. Related Requirements
- User Story: US-REC-003
- Acceptance Criteria: AC-5
- Non-Functional Requirements: NFR-3
- Business Rules: BR-5
- Functional Requirements: FR-3, FR-8

## 3. Preconditions
- Tenant "acme" (Tenant A) has applicant "Jordan Rivera" at "Applied".
- Tenant "globex" (Tenant B) has `manager@globex` with `Recruitment.Manage.All`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A applicant | Jordan Rivera ({acme_applicantId}) | Target of cross-tenant attempt |
| Actor | manager@globex (Manage in B) | Authorized in B only |
| Injected tenant_id | acme's id in request body | Must be ignored |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `manager@globex`, `PATCH /api/v1/recruitment/applications/{acme_applicantId}/stage` to "Screening" | 404/403; the EF tenant query filter prevents loading acme's applicant under globex; no stage change in acme. |
| 2 | Verify acme's applicant after the attempt | Jordan Rivera is still at "Applied"; no `applicant_stage_history` row was created in acme. |
| 3 | As `manager@globex`, send a stage move for a globex applicant but inject `tenant_id = acme` in the body | The body tenant_id is ignored; the move (if valid) affects only globex; the new `applicant_stage_history` row is stamped with globex's tenant_id (TenantInterceptor), never acme's. |
| 4 | Attempt a bulk stage move including `{acme_applicantId}` among globex ids | acme's applicant is not affected/visible; only globex applicants are updated; no cross-tenant write (FR-8). |
| 5 | Verify at the DB level | No acme `applicant` or `applicant_stage_history` rows were modified/created by globex requests; all globex-written history rows carry globex's tenant_id. |

## 6. Postconditions
- No cross-tenant stage move occurred; all history rows are stamped with the session tenant, not a client-supplied one.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
