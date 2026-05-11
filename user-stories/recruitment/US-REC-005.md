---
id: US-REC-005
module: Recruitment
priority: Must Have
persona: Recruiter
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-REC-005: Schedule Interviews and Notify Participants

## 1. Description
**As a** Recruiter,
**I want to** schedule interviews for applicants by selecting interviewers, date/time, location or video link, and have all participants automatically notified,
**So that** interviews are coordinated efficiently and no participant misses the scheduled session.

## 2. Preconditions
- The user is authenticated and has `Recruitment.Manage.All` permission within the resolved tenant.
- The applicant is in the "Interview" stage (or the recruiter is advancing them to "Interview" as part of scheduling).
- At least one employee exists in the tenant who can act as an interviewer.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An applicant is in the "Interview" stage | The recruiter creates an interview by selecting interviewer(s), date/time, duration, interview type (in-person/video/phone), and location or video link | The interview is saved, email notifications are sent to all interviewers and the applicant with the interview details, and a Hangfire reminder job is scheduled |
| AC-2 | An interview is scheduled | The scheduled date/time is 24 hours away | Hangfire fires a reminder notification (email + in-app) to all participants (interviewers and applicant) |
| AC-3 | An interview is scheduled | The recruiter edits the date/time or cancels the interview | All participants receive an updated/cancellation notification, and the Hangfire reminder job is rescheduled or removed accordingly |
| AC-4 | Multiple interview rounds are needed | The recruiter schedules a second interview for the same applicant with different interviewers | Both interviews are tracked independently with their own schedules, interviewers, and scorecards |
| AC-5 | An interview is scheduled in Tenant A | A user in Tenant B queries interview schedules | No interview data from Tenant A is visible; RLS enforces tenant isolation |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide an interview scheduling form with: applicant (pre-selected from context), interviewer(s) (multi-select employee dropdown, at least one required), date (required), start time (required), duration in minutes (required, default 60), interview type (in-person/video/phone, required), location (text, required for in-person), video meeting link (URL, required for video), and notes for interviewers (optional rich text).
- FR-2: The system SHALL support multiple interview rounds per applicant per vacancy, each tracked as a separate interview record with a round number.
- FR-3: The system SHALL send email notifications to all interviewers and the applicant upon interview creation, update, or cancellation, using tenant-configured notification templates.
- FR-4: The system SHALL schedule a Hangfire background job for sending reminder notifications 24 hours before the interview (configurable per tenant).
- FR-5: The system SHALL provide a calendar view of all scheduled interviews for the current tenant, filterable by interviewer, vacancy, date range, and status.
- FR-6: The system SHALL track interview status: Scheduled, Completed, Cancelled, No-Show.
- FR-7: The system SHALL detect scheduling conflicts (interviewer already has another interview at the same time) and warn the recruiter, but allow override.
- FR-8: The system SHALL allow the recruiter to attach an interview guide or evaluation criteria document to the interview.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Interview scheduling API SHALL respond within 800 ms (P95), including notification outbox writes.
- NFR-2: All interview data SHALL be tenant-scoped with `tenant_id` and protected by PostgreSQL RLS.
- NFR-3: Email notifications SHALL be sent asynchronously via Hangfire (not blocking the scheduling API call).
- NFR-4: Hangfire reminder jobs SHALL be idempotent and tenant-aware (tenant_id in job parameters).
- NFR-5: The interview calendar view SHALL be responsive and usable on mobile devices (360px+).
- NFR-6: Interview scheduling form SHALL validate date/time is in the future and within business hours (configurable).

## 6. Business Rules
- BR-1: Only users with `Recruitment.Manage.All` permission can schedule, edit, or cancel interviews.
- BR-2: Interviewers must be active employees within the same tenant.
- BR-3: An interview cannot be scheduled for a date in the past.
- BR-4: Cancelling an interview does not change the applicant's pipeline stage; the recruiter must explicitly reject or reschedule.
- BR-5: The reminder notification lead time (default 24 hours) is configurable at the tenant level via module configuration (S35.2.9).
- BR-6: If an interview is rescheduled, the original Hangfire reminder job is cancelled and a new one is created for the updated time.
- BR-7: Interview scheduling sends notifications to the applicant's email (from their application) and to each interviewer's work email (from their employee record).

## 7. Data Requirements
- **Input:** Applicant ID, vacancy ID, interviewer employee IDs (array), date, start time, duration (minutes), interview type (enum), location (text), video link (URL), notes (rich text), round number.
- **Output:** Interview record with UUID primary key, `tenant_id`, status (Scheduled), associated Hangfire job ID for the reminder.
- **Storage:** `interview` table: `id`, `tenant_id`, `applicant_id`, `vacancy_id`, `round_number`, `interview_type`, `scheduled_date`, `start_time`, `duration_minutes`, `location`, `video_link`, `notes`, `status`, `hangfire_job_id`, `created_by`, `created_at`. `interview_interviewer` junction table: `interview_id`, `employee_id`. RLS on `tenant_id`.

## 8. UI/UX Notes
- Interview scheduling form: slide-over panel from the applicant detail view or a modal dialog.
- Interviewer selection: searchable multi-select dropdown with employee avatar and department shown.
- Date/time picker: modern date picker with time slots; highlight conflicting times if an interviewer has another interview.
- Calendar view: Notion-like monthly/weekly calendar with interview blocks color-coded by status (Scheduled=blue, Completed=green, Cancelled=gray).
- Interview card on the applicant detail timeline: shows date, time, interviewers (avatars), type badge, and status badge.
- Mobile: full-width scheduling form; calendar view switches to a list/agenda view on screens < 768px.
- Notification preview: show a preview of the email that will be sent to participants before confirming the schedule.

## 9. Dependencies
- US-REC-002 (applicant exists).
- US-REC-004 (applicant is in "Interview" stage or being moved to it).
- Core HR module (employee records for interviewer selection).
- Notification System (S25) for email and in-app notifications.
- Hangfire (S28) for reminder job scheduling.
- Tenant module configuration for reminder lead time and notification templates.

## 10. Assumptions & Constraints
- The system does not integrate with external calendar systems (Google Calendar, Outlook) in Phase 1; this is a future enhancement.
- Video meeting links are manually entered by the recruiter; the system does not auto-create meetings.
- Interview scheduling does not check interviewer availability against leave/attendance systems in Phase 1 (future enhancement).
- The reminder Hangfire job stores the `tenant_id` to ensure it runs in the correct tenant context.
- Notification templates support variable substitution: applicant name, vacancy title, interview date/time, location, video link, interviewer names.

## 11. Test Hints
- Schedule an interview and verify all participants (interviewers + applicant) receive email notifications.
- Verify Hangfire reminder job is created with the correct fire time (24 hours before interview).
- Reschedule the interview: verify the old reminder job is cancelled and a new one is created.
- Cancel the interview: verify cancellation notifications are sent and the reminder job is removed.
- Test scheduling conflict detection: schedule two interviews for the same interviewer at the same time.
- Test multiple rounds: schedule Round 1 and Round 2 for the same applicant with different interviewers.
- Test cross-tenant isolation: verify Tenant B cannot see Tenant A's interviews via API.
- Test past-date validation: attempt to schedule an interview in the past and verify rejection.
- Test mobile responsiveness: verify the calendar/agenda view works at 360px.
