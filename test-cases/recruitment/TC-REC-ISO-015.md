---
id: TC-REC-ISO-015
user_story: US-REC-006
module: Recruitment
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-REC-ISO-015: Tenant B cannot read or write Tenant A's interview scorecards / criterion ratings; scorecard rows are session-tenant-stamped (AC-4, NFR-2)

## 1. Test Objective
Verify AC-4 / NFR-2 for REC-006's new surface: the `interview_scorecard` and `scorecard_criterion_rating` tables are tenant-isolated on BOTH reads and writes. A user in Tenant B cannot read Tenant A's scorecards (scores, comments, recommendations, averages), cannot submit/edit a scorecard against a Tenant A interview, and any scorecard row written carries the SESSION-derived tenant_id (via TenantInterceptor), never a client value. The consolidated aggregate average for an interview is computed ONLY over same-tenant scorecards. This exercises EF Core global query filters on `interview_scorecard`/`scorecard_criterion_rating` for the new mutation/read. Generic no/invalid tenant-context rejection and the cross-tenant write-block + body-injected-tenant_id contract are reused from TC-REC-ISO-010/011 on the recruitment surface (per the module's ISO-reuse convention).

NOTE: AC-4/NFR-2 specify PostgreSQL RLS; the platform enforces isolation via EF Core global query filters + TenantInterceptor -- if RLS is later added on `interview_scorecard`/`scorecard_criterion_rating`, extend Step 6 to assert it at the DB session level.

## 2. Related Requirements
- User Story: US-REC-006
- Acceptance Criteria: AC-4
- Non-Functional Requirements: NFR-2 (tenant-scoped + RLS), NFR-4 (analytics aggregations tenant-scoped)
- Functional Requirements: FR-1 (criterion ratings), FR-3 (average), FR-7 (audit)
- Reuses: TC-REC-ISO-010 (no/invalid/mismatched tenant context), TC-REC-ISO-011 (cross-tenant write block + body-injected tenant_id) on the recruitment surface.

## 3. Preconditions
- Tenant "acme" (A): interview {acme_interviewId} with empA's submitted scorecard {acme_scorecardId} (scores 4/5/3/4, comments, recommendation=Hire, average 4.0) + its criterion rows.
- Tenant "globex" (B): `interviewer@globex` assigned to a globex interview {globex_interviewId}, plus `manager@globex` (Recruitment.Manage.All).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Holds the target scorecard + criterion rows |
| Tenant B | globex | Auth context |
| A scorecard | {acme_scorecardId} | has criterion ratings, comments, recommendation |
| A interview | {acme_interviewId} | scorecard target |
| Injected tenant_id | acme's id in request body | Must be ignored |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `manager@globex`, `GET` the scorecards for {acme_interviewId} | Zero of acme's scorecards are returned; no scores/comments/recommendations/averages from acme are exposed (EF global query filter) (AC-4). |
| 2 | As `manager@globex`, `GET /scorecards/{acme_scorecardId}` directly | 404; acme's scorecard + its criterion ratings are not retrievable cross-tenant. |
| 3 | As `interviewer@globex`, attempt to submit/edit a scorecard against {acme_interviewId} | 404/403; the EF filter prevents loading acme's interview; no scorecard or criterion rows written in acme (reuses TC-REC-ISO-011). |
| 4 | As `manager@globex` / `interviewer@globex`, with no/invalid/mismatched tenant context, call the scorecard endpoints | Rejected (no tenant context resolved); no cross-tenant read/write (reuses TC-REC-ISO-010). |
| 5 | As `interviewer@globex`, submit a VALID scorecard for {globex_interviewId} but inject `tenant_id=acme` in the body | The body tenant_id is ignored; the new `interview_scorecard` + `scorecard_criterion_rating` rows are stamped globex (TenantInterceptor), never acme's (reuses TC-REC-ISO-011). |
| 6 | Verify at the DB level + aggregate scoping | `SELECT * FROM interview_scorecard WHERE tenant_id = globex_id` returns only globex rows; acme scorecards/criterion rows are invisible under globex; the consolidated aggregate average for any interview is computed over same-tenant scorecards only. (If RLS exists, confirm a globex session cannot read acme `interview_scorecard`/`scorecard_criterion_rating` rows via direct SQL.) |

## 6. Postconditions
- No cross-tenant scorecard, criterion-rating, or aggregate data was read or written; all scorecard rows carry the session tenant.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
