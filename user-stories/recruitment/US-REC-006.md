---
id: US-REC-006
module: Recruitment
priority: Must Have
persona: Interviewer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 4
---

# US-REC-006: Interviewer Submits Structured Interview Scorecard

## 1. Description
**As an** Interviewer (Manager or designated employee),
**I want to** submit a structured scorecard with ratings on predefined criteria and written feedback after conducting an interview,
**So that** the recruitment team has objective, comparable evaluation data to make informed hiring decisions.

## 2. Preconditions
- The user is authenticated and is assigned as an interviewer for the specific interview.
- The interview exists and has status `Scheduled` or `Completed`.
- The tenant has configured interview score sheet criteria (or is using defaults) via module configuration (S35.2.9).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An interviewer is assigned to a scheduled interview | They navigate to the interview from their dashboard or notification link and fill in the scorecard (rating each criterion on a 1-5 scale and adding comments) and click "Submit" | The scorecard is saved, the interview status updates to `Completed` (if all assigned interviewers have submitted), and the recruiter is notified |
| AC-2 | An interviewer submits a scorecard | The recruiter views the applicant detail panel | The scorecard is visible with individual criterion scores, overall average score, and the interviewer's written feedback |
| AC-3 | Multiple interviewers are assigned to the same interview | Each interviewer submits their own independent scorecard | All scorecards are stored separately and the applicant detail view shows a consolidated score (average across all interviewers) alongside individual breakdowns |
| AC-4 | An interviewer in Tenant A submits a scorecard | A user in Tenant B queries scorecards | No scorecard data from Tenant A is visible; tenant isolation is enforced via RLS |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide a scorecard form with: predefined evaluation criteria (configured per tenant, e.g., Technical Skills, Communication, Problem Solving, Cultural Fit, Leadership), each with a rating scale (1-5, labeled: 1=Poor, 2=Below Average, 3=Average, 4=Good, 5=Excellent), a comments field per criterion (optional), and an overall recommendation (Strong Hire, Hire, No Hire, Strong No Hire).
- FR-2: The system SHALL allow each interviewer to submit exactly one scorecard per interview assignment; subsequent edits are allowed until a configurable lock period (e.g., 48 hours after interview).
- FR-3: The system SHALL calculate and display the average score across all criteria for each scorecard, and an aggregate average across all interviewers for the same interview round.
- FR-4: The system SHALL mark the interview as `Completed` when all assigned interviewers have submitted their scorecards.
- FR-5: The system SHALL notify the recruiter (in-app + optional email via Hangfire) when a scorecard is submitted.
- FR-6: The system SHALL prevent interviewers from viewing other interviewers' scorecards for the same interview until they have submitted their own (to prevent bias).
- FR-7: The system SHALL log scorecard submissions in the audit trail.
- FR-8: The system SHALL display scorecard data on the applicant detail panel with a visual comparison (e.g., radar chart or bar chart) when multiple scorecards exist.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Scorecard submission API SHALL respond within 800 ms (P95).
- NFR-2: All scorecard data SHALL be tenant-scoped with `tenant_id` and protected by PostgreSQL RLS.
- NFR-3: The scorecard form SHALL be mobile-responsive (360px+) to allow interviewers to submit scores from a mobile device immediately after an interview.
- NFR-4: Scorecard data SHALL be included in recruitment analytics aggregations (US-REC-009) with sub-second query performance for up to 1000 scorecards per tenant.

## 6. Business Rules
- BR-1: Only the assigned interviewer can submit a scorecard for their interview assignment; other users cannot submit on their behalf.
- BR-2: Evaluation criteria are configurable per tenant via the interview score sheets configuration (S35.2.9); defaults are provided (Technical Skills, Communication, Problem Solving, Cultural Fit).
- BR-3: The overall recommendation is mandatory; the recruiter uses this as a key signal for advancement decisions.
- BR-4: Scorecards are immutable after the lock period (48 hours post-interview by default, configurable); edits within the window create a version history.
- BR-5: An interviewer cannot view other interviewers' scorecards until their own is submitted (anti-bias measure).
- BR-6: The scorecard submission serves as a gate criterion for advancing to the "Offer" stage (US-REC-004, FR-1).

## 7. Data Requirements
- **Input:** Interview ID, interviewer employee ID (from auth context), criterion ratings (array of { criterion_id, score (1-5), comment }), overall recommendation (enum), general notes (optional text).
- **Output:** Scorecard record with UUID primary key, `tenant_id`, average score (calculated), submitted_at timestamp.
- **Storage:** `interview_scorecard` table: `id`, `tenant_id`, `interview_id`, `interviewer_employee_id`, `overall_recommendation`, `average_score` (computed), `general_notes`, `submitted_at`, `locked_at`. `scorecard_criterion_rating` table: `id`, `scorecard_id`, `criterion_id`, `score`, `comment`. RLS on `tenant_id`.

## 8. UI/UX Notes
- Scorecard form: clean, focused layout -- one criterion per row with a star rating or numeric selector (1-5) and an inline comment expand toggle.
- Overall recommendation: prominent radio buttons or segmented control (Strong Hire / Hire / No Hire / Strong No Hire) with color coding (green/light green/orange/red).
- Submission confirmation: show a summary of scores before final submission with an "Edit" option.
- Recruiter's view: consolidated scorecard table showing each interviewer's scores side-by-side, with the aggregate average highlighted. Optional radar chart for visual comparison.
- Notion-like aesthetic: minimal form chrome, generous whitespace, subtle animations on score selection.
- Mobile: stack criteria vertically, large touch targets for ratings, collapsible comment fields.
- Anti-bias: if the interviewer has not yet submitted, other scorecards for the same interview are hidden behind a "Submit your scorecard first" overlay.

## 9. Dependencies
- US-REC-005 (interview must be scheduled and the interviewer assigned).
- US-REC-004 (scorecard is a gate criterion for advancing to Offer stage).
- Tenant module configuration for evaluation criteria (S35.2.9).
- Notification System (S25) for recruiter notifications on scorecard submission.
- Audit Logging module.

## 10. Assumptions & Constraints
- Evaluation criteria are shared across all vacancies within a tenant in Phase 1; per-vacancy criteria customization is a future enhancement.
- The 1-5 rating scale is fixed in Phase 1; configurable scales (e.g., 1-10) are a future enhancement.
- Scorecard data is not anonymized -- the recruiter can see which interviewer submitted which scores.
- The system does not enforce a minimum number of scorecards before allowing advancement; the gate only requires at least one.

## 11. Test Hints
- Submit a scorecard with all criteria rated and verify the average score is correctly calculated.
- Test the anti-bias rule: as Interviewer B, try to view Interviewer A's scorecard before submitting your own; verify it is hidden.
- Submit a scorecard and verify the recruiter receives an in-app notification.
- Test the lock period: edit a scorecard within 48 hours (success); edit after 48 hours (rejected).
- Verify the interview status changes to `Completed` only when all assigned interviewers have submitted.
- Test cross-tenant isolation: verify Tenant B cannot access Tenant A's scorecards via API.
- Test mobile layout at 360px: verify the rating controls are usable with touch input.
- Test that scorecard submission satisfies the Offer stage gate criterion (US-REC-004).
