---
id: TC-REC-ISO-014
user_story: US-REC-005
module: Recruitment
priority: critical
type: security
status: draft
created: 2026-06-15
---

# TC-REC-ISO-014: Tenant B cannot read or write Tenant A's interviews/interviewers/reminder jobs; interview rows + reminder jobs are session-tenant-stamped (AC-5, NFR-2, NFR-4)

## 1. Test Objective
Verify AC-5 / NFR-2 / NFR-4 for REC-005's new surface: the `interview` and `interview_interviewer` tables and the Hangfire reminder jobs are tenant-isolated on BOTH reads and writes. A user in Tenant B cannot list/read Tenant A's interview schedules or calendar, cannot edit/cancel a Tenant A interview, cannot assign a Tenant A employee as an interviewer, and any interview row written carries the SESSION-derived tenant_id (via TenantInterceptor), never a client value. The reminder Hangfire job carries tenant_id in its parameters and runs ONLY in its own tenant's context. This adds the interview/reminder-job dimension specific to US-REC-005. Generic no/invalid tenant-context rejection and the cross-tenant write-block + body-injected-tenant_id contract are reused from TC-REC-ISO-010/011 on the recruitment surface (per the module's ISO-reuse convention).

NOTE: AC-5/NFR-2 specify PostgreSQL RLS; the platform enforces isolation via EF Core global query filters + TenantInterceptor -- if RLS is later added on `interview`/`interview_interviewer`, extend Step 6 to assert it at the DB session level.

## 2. Related Requirements
- User Story: US-REC-005
- Acceptance Criteria: AC-5
- Non-Functional Requirements: NFR-2 (tenant-scoped + RLS), NFR-4 (tenant-aware reminder jobs)
- Functional Requirements: FR-1 (interviewers), FR-4 (reminder job), FR-5 (calendar)
- Reuses: TC-REC-ISO-010 (no/invalid/mismatched tenant context), TC-REC-ISO-011 (cross-tenant write block + body-injected tenant_id) on the recruitment surface.

## 3. Preconditions
- Tenant "acme" (A): a Scheduled interview {acme_interviewId} (round 1) with interviewers [empA, empB], applicant Jordan Rivera, and a pending reminder job {acme_jobId}.
- Tenant "globex" (B): `manager@globex` with `Recruitment.Manage.All`, plus a globex employee {empGlobex}.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Holds the target interview + reminder job |
| Tenant B | globex | Auth context |
| A interview | {acme_interviewId} | Scheduled, has interviewers + reminder |
| A reminder job | {acme_jobId} | tenant_id=acme in params |
| Injected tenant_id | acme's id in request body | Must be ignored |
| Cross-tenant interviewer | {empGlobex} into an acme interview | Must be blocked |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `manager@globex`, `GET` the interview list / calendar | Zero of acme's interviews are returned; the calendar shows only globex interviews (AC-5, FR-5). |
| 2 | As `manager@globex`, `GET /interviews/{acme_interviewId}` directly | 404; acme's interview details (interviewers, applicant, link/location, notes) are not exposed (EF global query filter). |
| 3 | As `manager@globex`, attempt to reschedule or cancel {acme_interviewId} | 404/403; the EF filter prevents loading acme's interview; no change, no notification, no reminder mutation in acme (reuses TC-REC-ISO-011). |
| 4 | As `manager@globex`, schedule a globex interview but inject interviewerIds=[{empGlobex}, {acme_empA}] | The acme employee is rejected as ineligible (not visible in globex); only globex-eligible interviewers persist (BR-2 + isolation). |
| 5 | As `manager@globex`, perform a VALID globex schedule but inject `tenant_id=acme` in the body | The body tenant_id is ignored; the new `interview` row + `interview_interviewer` rows are stamped globex (TenantInterceptor); the reminder job carries tenant_id=globex in its parameters (NFR-4) (reuses TC-REC-ISO-011). |
| 6 | Verify at the DB / job level | `SELECT * FROM interview WHERE tenant_id = globex_id` returns only globex rows; acme interviews are invisible under globex; {acme_jobId} carries tenant_id=acme and executes only under acme context. (If RLS exists, confirm a globex session cannot read acme `interview`/`interview_interviewer` rows via direct SQL.) |

## 6. Postconditions
- No cross-tenant interview, interviewer, or reminder-job data was read or written; all interview rows + reminder jobs carry the session tenant.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
