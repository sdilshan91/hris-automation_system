---
id: US-PAY-011
module: Payroll
priority: Should Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-PAY-011: Bulk Payslip Email Distribution

## 1. Description
**As an** HR Officer,
**I want to** send payslip PDFs via email to all employees in bulk after a payroll run is finalized,
**So that** employees receive their payslip documents promptly without having to log in to the portal.

## 2. Preconditions
- A payroll run is in Finalized status (US-PAY-008).
- Payslip PDFs have been generated for all employees in the run (US-PAY-004).
- Employee email addresses are on file in the Core HR module.
- Tenant email server/SMTP relay is configured (technical doc section 3.3 assumption).
- Notification templates for payslip emails are configured.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | A payroll run is Finalized with PDFs generated | HR clicks "Send Payslips" on the payroll run detail page | A Hangfire job is enqueued to send individual emails with PDF attachments to all employees in the run; the API returns 202 Accepted |
| AC-2 | The email distribution job is processing | Emails are sent | Each employee receives an email with their personal payslip PDF attached; the email uses the tenant-branded notification template with subject line: "Your Payslip for {Month} {Year}" |
| AC-3 | An employee does not have an email address on file | The distribution job processes that employee | The employee is skipped with a warning logged; the job continues for remaining employees; a summary of skipped employees is shown to HR |
| AC-4 | Email sending fails for some employees (e.g., SMTP error) | The job encounters failures | Failed deliveries are retried up to 3 times with exponential backoff (via Polly); permanently failed deliveries are logged with error details and surfaced to HR |
| AC-5 | Payslip emails are sent for Tenant A | The emails contain only Tenant A's data | Each email contains only the recipient employee's payslip; no cross-employee or cross-tenant data leakage; email sender address uses tenant's configured domain |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide a "Send Payslips" action on finalized payroll runs that enqueues a `SendPayslipEmailsJob` via Hangfire.
- FR-2: The Hangfire job SHALL iterate over all employees in the payroll run, retrieve their payslip PDF from blob storage, and send an individual email with the PDF attached.
- FR-3: The system SHALL use the tenant's configured notification template for payslip emails, supporting variables: {EmployeeName}, {PayMonth}, {PayYear}, {NetSalary}, {CompanyName}.
- FR-4: The system SHALL support selective re-sending: HR can re-send payslips to specific employees (e.g., those who had delivery failures or newly added email addresses).
- FR-5: The system SHALL track email delivery status per employee: Queued, Sent, Failed, with timestamps.
- FR-6: The system SHALL rate-limit email sending to comply with SMTP provider limits (configurable, e.g., 100 emails per minute).
- FR-7: The system SHALL prevent duplicate sends: if payslips have already been sent for a run, HR must confirm before re-sending.
- FR-8: The Hangfire job SHALL restore `ITenantContext` from job arguments and operate within tenant scope.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Bulk email distribution for 5,000 employees SHALL complete within 30 minutes (rate-limited by SMTP provider capacity).
- NFR-2: Email sending SHALL use Polly retry policies with exponential backoff (per dev-instructions: Polly for resilience).
- NFR-3: The email job SHALL be idempotent: re-running after a partial failure picks up from where it left off (per technical doc section 28.5).
- NFR-4: Payslip PDF attachments SHALL not exceed 200KB per email to avoid delivery issues.
- NFR-5: Email content SHALL not include salary amounts in the email body (security); amounts are only in the attached PDF.
- NFR-6: Test coverage for email distribution logic SHALL be >= 85%.

## 6. Business Rules
- BR-1: Payslip emails can only be sent for Finalized payroll runs. Attempting to send from a non-finalized run is rejected.
- BR-2: The email subject and body are configurable per tenant via notification templates. The default template is provided by the system.
- BR-3: Employees who have opted out of email notifications (if such a preference exists) must still have their payslip available in the portal but should not receive the email.
- BR-4: The "From" address for payslip emails should use the tenant's configured sender address (e.g., payroll@acme.yourhrm.com) if available, or the system default.
- BR-5: HR must have explicit confirmation before re-sending payslips that were already sent, to avoid employee confusion from duplicate emails.
- BR-6: Email distribution status is tracked per payroll run; HR can view a summary: Total, Sent, Failed, Skipped.
- BR-7: Terminated employees whose final payslip is in the run must receive their payslip email if their email is still on file.

## 7. Data Requirements

**payslip_email_log table:**
| Column | Type | Constraints |
|--------|------|-------------|
| email_log_id | uuid (PK) | NOT NULL |
| tenant_id | uuid (FK) | NOT NULL, RLS-enforced |
| payroll_run_id | uuid (FK) | NOT NULL |
| payroll_slip_id | uuid (FK) | NOT NULL |
| employee_id | uuid (FK) | NOT NULL |
| recipient_email | varchar(255) | NOT NULL |
| status | varchar(20) | NOT NULL (Queued, Sent, Failed, Skipped) |
| sent_at | timestamptz | nullable |
| failure_reason | text | nullable |
| retry_count | int | default 0 |
| created_at | timestamptz | NOT NULL |

**Distribution Summary (for UI):**
| Field | Description |
|-------|-------------|
| total_employees | Total in the run |
| emails_sent | Successfully sent |
| emails_failed | Failed after retries |
| emails_skipped | No email on file |
| emails_queued | Pending send |
| started_at | Job start time |
| completed_at | Job completion time |

## 8. UI/UX Notes (Notion-like)
- "Send Payslips" button on the payroll run detail page, enabled only for Finalized runs with generated PDFs. Styled as a primary action button with an email icon.
- Confirmation dialog before sending: "You are about to send payslip emails to {count} employees. This action cannot be undone. Continue?"
- Distribution progress: real-time progress bar via SignalR showing sent/total count.
- Distribution summary card after completion: green/red/amber counts for Sent/Failed/Skipped with expandable lists.
- "Re-send" action available per employee (for failed deliveries) or as bulk "Re-send All Failed".
- Mobile: HR can initiate send and view status; progress updates via push notification.

## 9. Dependencies
- **US-PAY-004**: Payslip PDFs must be generated before email distribution.
- **US-PAY-008**: Payroll run must be Finalized.
- **Notification System**: Technical doc section 25 -- email sending infrastructure.
- **Hangfire**: Background job for async email distribution (per dev-instructions).
- **Polly**: Retry policies for transient SMTP failures (per dev-instructions).
- SMTP relay or transactional email service (per technical doc section 3.3).

## 10. Assumptions & Constraints
- SMTP relay or transactional email service is available and configured (technical doc assumption).
- Email rate limits are tenant-configurable based on the SMTP provider's capacity.
- Payslip PDFs are pre-generated; the email job does not generate PDFs on the fly.
- Email delivery confirmation is based on SMTP acceptance, not actual inbox delivery (delivery tracking beyond SMTP is out of scope).
- Large attachments (> 200KB) are flagged during PDF generation, not at email send time.

## 11. Test Hints
- Unit test: Verify email job skips employees without email addresses and logs warnings.
- Unit test: Verify retry logic with Polly: simulate SMTP failure, verify 3 retries with exponential backoff.
- Unit test: Verify duplicate send prevention: second invocation for same run requires confirmation flag.
- Integration test: Send payslip email for one employee, verify email is delivered (using a test SMTP server like MailHog or Papercut).
- Integration test: Verify email log records are created with correct status for each employee.
- Integration test: Verify tenant isolation: email job for Tenant A does not send Tenant B's payslips.
- E2E (Playwright): Finalize payroll run, click "Send Payslips", verify progress bar, verify completion summary.
- Load test: Simulate bulk send for 5,000 employees with rate limiting and verify completion within 30 minutes.
