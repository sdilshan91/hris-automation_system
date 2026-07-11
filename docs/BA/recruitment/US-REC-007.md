---
id: US-REC-007
module: Recruitment
priority: Must Have
persona: Recruiter
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-REC-007: Generate and Send Offer Letter

## 1. Description
**As a** Recruiter,
**I want to** generate an offer letter from a configurable template with applicant and position details, review it, and send it to the applicant,
**So that** the organization can formally extend a job offer and the applicant can accept or decline.

## 2. Preconditions
- The user is authenticated and has `Recruitment.Offer.All` permission within the resolved tenant.
- The applicant is in the "Offer" stage of the pipeline.
- The tenant has at least one offer letter template configured (S35.2.9).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An applicant is in the "Offer" stage | The recruiter selects an offer template, fills in offer details (salary, start date, position, department, reporting manager, benefits summary), and clicks "Generate" | A PDF offer letter is generated using the template with variable substitution, stored in blob storage at the tenant-scoped path, and displayed for preview |
| AC-2 | An offer letter PDF has been generated and previewed | The recruiter clicks "Send to Applicant" | The offer letter is emailed to the applicant's email address with the PDF attached, the offer record status changes to `Sent`, and a Hangfire job is scheduled for offer expiry follow-up |
| AC-3 | An offer has been sent | The applicant responds (accept or decline) via the candidate portal or the recruiter records the response manually | The offer status updates to `Accepted` or `Declined`; if accepted, the applicant advances to "Hired" stage; if declined, the applicant remains in "Offer" stage with status noted |
| AC-4 | An offer has been sent | The offer expiry date passes without a response | Hangfire triggers a reminder notification to the recruiter and optionally to the applicant; after a configurable grace period, the offer status changes to `Expired` |
| AC-5 | An offer is generated in Tenant A | A user in Tenant B queries offers | No offer data from Tenant A is visible; RLS enforces complete tenant isolation |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide an offer creation form with: offer template selection (dropdown of tenant-configured templates), offered position/job title, department, reporting manager, salary (amount + currency + frequency), benefits summary (text), start date, offer expiry date, probation period, and custom clauses (optional text).
- FR-2: The system SHALL generate the offer letter as a PDF using the selected template with variable substitution (applicant name, position, salary, start date, company name, etc.) using QuestPDF or equivalent free open-source library.
- FR-3: The system SHALL store the generated offer letter PDF in blob storage at the tenant-scoped path: `{tenantId}/recruitment/{vacancyId}/{applicantId}/offers/{filename}`.
- FR-4: The system SHALL provide a preview of the generated PDF before sending.
- FR-5: The system SHALL send the offer letter via email (with PDF attachment) using the tenant's notification system.
- FR-6: The system SHALL track offer status: `Draft`, `Sent`, `Accepted`, `Declined`, `Expired`, `Withdrawn`.
- FR-7: The system SHALL schedule Hangfire jobs for: offer expiry reminder (configurable days before expiry), offer expiry auto-status change.
- FR-8: The system SHALL support offer withdrawal by the recruiter at any point before acceptance, with a notification sent to the applicant.
- FR-9: The system SHALL support multiple offer versions (e.g., if salary is renegotiated, a new offer can be generated, superseding the previous one).
- FR-10: The system SHALL integrate with the approval workflow engine (S34) if the tenant requires offer approval before sending (e.g., manager or HR head approval).

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Offer letter PDF generation SHALL complete within 3 seconds for a standard 2-page template.
- NFR-2: All offer data SHALL be tenant-scoped with `tenant_id` and protected by PostgreSQL RLS.
- NFR-3: Offer letter PDFs SHALL be stored encrypted at rest in blob storage.
- NFR-4: Email delivery SHALL be asynchronous via Hangfire; the API SHALL return success after queuing the notification.
- NFR-5: The offer letter template editor (tenant admin) SHALL support rich text with placeholders and be mobile-responsive.

## 6. Business Rules
- BR-1: Only users with `Recruitment.Offer.All` permission can generate, send, and manage offers.
- BR-2: An applicant can have only one active (non-expired, non-declined, non-withdrawn) offer per vacancy at a time.
- BR-3: Offer acceptance triggers automatic advancement to the "Hired" pipeline stage.
- BR-4: Offer letter templates are managed by the Tenant Admin and support variable placeholders: `{{applicant_name}}`, `{{position}}`, `{{department}}`, `{{salary}}`, `{{start_date}}`, `{{company_name}}`, `{{expiry_date}}`, etc.
- BR-5: If the tenant requires offer approval (configurable), the offer cannot be sent until the approval workflow completes.
- BR-6: Offer expiry date is mandatory; default is 7 days from generation (configurable per tenant).
- BR-7: A withdrawn offer cannot be re-sent; a new offer must be generated.

## 7. Data Requirements
- **Input:** Applicant ID, vacancy ID, template ID, offered position, department ID, salary (amount, currency, frequency), benefits text, start date, expiry date, reporting manager employee ID, probation period (months), custom clauses.
- **Output:** Offer record with UUID, `tenant_id`, status, generated PDF storage key, sent_at, accepted_at/declined_at timestamps, offer reference number.
- **Storage:** `offer` table: `id`, `tenant_id`, `applicant_id`, `vacancy_id`, `template_id`, `status`, `offered_salary`, `currency`, `start_date`, `expiry_date`, `pdf_storage_key`, `sent_at`, `responded_at`, `response`, `version`, `created_by`, `created_at`. RLS on `tenant_id`. PDF in blob storage.

## 8. UI/UX Notes
- Offer creation: multi-step form or side-panel with sections: Position Details, Compensation, Timeline, Additional Clauses.
- Template selection: dropdown with template preview thumbnail.
- PDF preview: inline viewer (pdf.js) with "Download" and "Send" action buttons.
- Offer tracking: status badges on the applicant card (Draft=gray, Sent=blue, Accepted=green, Declined=red, Expired=orange, Withdrawn=gray-strikethrough).
- Offer timeline on applicant detail: shows generated -> sent -> response sequence with timestamps.
- Notion-like design: clean, focused forms with clear call-to-action buttons; salary field with currency selector and pay frequency indicator.
- Mobile: full-width form, PDF preview in a modal with pinch-to-zoom support.

## 9. Dependencies
- US-REC-004 (applicant must be in "Offer" stage).
- US-REC-006 (interview scorecards inform offer decisions).
- US-REC-010 (offer acceptance triggers employee conversion).
- Tenant module configuration for offer templates (S35.2.9).
- File & Document Management (S26) for PDF storage.
- Notification System (S25) for email delivery.
- Hangfire (S28) for expiry reminders and auto-status changes.
- Approval Workflow Engine (S34) if offer approval is required.
- QuestPDF or equivalent for PDF generation.

## 10. Assumptions & Constraints
- Offer letter templates are HTML-based with variable placeholders, rendered to PDF server-side.
- Digital/electronic signature of the offer letter is out of scope for Phase 1; acceptance is recorded as a status change.
- Salary negotiation is handled informally (recruiter updates the offer details and generates a new version); there is no formal negotiation workflow.
- The offer letter PDF includes the tenant's branding (logo, colors) from the tenant configuration.

## 11. Test Hints
- Generate an offer letter PDF and verify all variable placeholders are correctly substituted.
- Send an offer and verify the email is delivered with the PDF attachment.
- Test offer expiry: set expiry to 1 minute in the future, wait, and verify the Hangfire job changes the status to `Expired`.
- Test offer acceptance: verify the applicant advances to "Hired" stage and the offer status is `Accepted`.
- Test offer decline: verify the applicant stays in "Offer" stage with offer status `Declined`.
- Test offer withdrawal: verify the applicant is notified and the offer cannot be re-sent.
- Test multiple offer versions: generate a second offer for the same applicant/vacancy and verify the first is superseded.
- Test cross-tenant isolation: verify Tenant B cannot access Tenant A's offer letters or PDFs.
- Test PDF generation performance: generate a 2-page offer letter and verify it completes within 3 seconds.
- Test offer approval workflow (if configured): verify offer cannot be sent before approval is granted.
