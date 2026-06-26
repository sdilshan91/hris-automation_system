---
id: TC-REC-ISO-013
user_story: US-REC-004
module: Recruitment
priority: critical
type: security
status: fail
created: 2026-06-15
---

# TC-REC-ISO-013: Tenant B cannot read or write Tenant A's stage-history / transitions / rejection reasons; history rows are session-tenant-stamped (AC-5, NFR-2)

## 1. Test Objective
Verify AC-5 / NFR-2 for REC-004's transition surface: the full stage-history audit trail (from_stage, to_stage, changed_by, changed_at, notes, rejection_reason) is tenant-isolated on BOTH reads and writes. A user in Tenant B cannot query Tenant A's transition history/timeline, cannot advance/reject/reactivate a Tenant A applicant, and any history row written carries the SESSION-derived tenant_id (via TenantInterceptor), never a client value. This exercises EF Core global query filters on `applicant_stage_history` for the new multi-stage/reject/reactivate operations. (Generic single-move read/context/write isolation is already covered by TC-REC-ISO-009/010/011 on the same tables and is reused here per the module's ISO-reuse convention; this TC adds the rejection-reason + multi-transition-trail dimension specific to US-REC-004. NOTE: AC-5/NFR-2 specify PostgreSQL RLS; the platform enforces isolation via EF Core global query filters + TenantInterceptor -- if RLS is later added on `applicant_stage_history`, extend Step 6 to assert it at the DB session level.)

## 2. Related Requirements
- User Story: US-REC-004
- Acceptance Criteria: AC-5
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-3 (rejection), FR-4 (history row), FR-5 (backward/reactivation)
- Reuses: TC-REC-ISO-009 (pipeline read), TC-REC-ISO-010 (no tenant context), TC-REC-ISO-011 (cross-tenant write block) on the same applicant/stage-history tables.

## 3. Preconditions
- Tenant "acme" (A): applicant "Jordan Rivera" with a multi-stage history (Applied->Screening->Interview) and a rejected applicant with rejection_reason=NotQualified.
- Tenant "globex" (B): `manager@globex` with `Recruitment.Manage.All`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Holds the target history + rejection rows |
| Tenant B | globex | Auth context |
| A applicant (multi-stage) | {acme_applicantId} | Has 2+ history rows |
| A rejected applicant | {acme_rejectedId} | rejection_reason set |
| Injected tenant_id | acme's id in request body | Must be ignored |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As globex, `GET` Tenant A applicant's stage-history/timeline ({acme_applicantId}) | 404; none of acme's history rows (stages, reasons, changed_by) are returned (AC-5). |
| 2 | As globex, query/list transitions filtered to see any acme rejection_reason | Zero acme rows; rejection reasons are not visible cross-tenant. |
| 3 | As `manager@globex`, attempt to reject {acme_rejectedId-or-active} with a reason | 404/403; the EF filter prevents loading acme's applicant; no history row, no rejection_reason written in acme. |
| 4 | As `manager@globex`, attempt to reactivate or backward-move an acme applicant | 404/403; reactivation/backward move on a cross-tenant applicant is blocked (FR-5). |
| 5 | As `manager@globex`, perform a VALID transition on a globex applicant but inject `tenant_id=acme` in the body | The body tenant_id is ignored; the new `applicant_stage_history` row is stamped with globex's tenant_id (TenantInterceptor), never acme's. |
| 6 | Verify at the DB level | `SELECT * FROM applicant_stage_history WHERE tenant_id = globex_id` returns only globex rows; acme's transition/rejection rows are invisible under globex; all globex-written rows carry globex's tenant_id. (If RLS exists, confirm a globex session cannot read acme rows via direct SQL.) |

## 6. Postconditions
- No cross-tenant stage-history, transition, or rejection-reason data was read or written; all history rows carry the session tenant.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
