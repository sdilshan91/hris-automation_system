---
id: US-REC-008
module: Recruitment
priority: Should Have
persona: Applicant
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 4
---

# US-REC-008: Applicant Tracks Application Status (Candidate Portal)

## 1. Description
**As an** Applicant,
**I want to** check the current status of my application, view upcoming interview details, and respond to offers through a candidate portal,
**So that** I stay informed about my progress and can take timely actions without contacting the recruiter directly.

## 2. Preconditions
- The applicant has submitted at least one application to a vacancy within the tenant.
- The applicant has received a confirmation email with a unique, secure portal access link (or has registered via email verification).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An applicant has submitted an application | They access the candidate portal via the secure link sent in the confirmation email | They see a dashboard showing their application(s) with the current pipeline stage displayed as a visual progress tracker (e.g., step indicator: Applied -> Screening -> Interview -> Offer -> Hired) |
| AC-2 | An applicant has an interview scheduled | They view their application on the candidate portal | The upcoming interview details are displayed: date, time, type (in-person/video/phone), location or video link, and interviewer name(s) |
| AC-3 | An applicant has received an offer | They view the offer on the candidate portal | The offer details (position, salary, start date, expiry date) and the offer letter PDF are available for download, along with "Accept" and "Decline" action buttons |
| AC-4 | An applicant from Tenant A accesses their portal | They attempt to access data from Tenant B | The portal only shows applications within the tenant context resolved from the subdomain; no cross-tenant data is accessible |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL provide a candidate portal accessible via a unique, time-limited, cryptographically signed URL sent in the application confirmation email (magic link pattern).
- FR-2: The portal SHALL display a list of the applicant's applications within the tenant, each showing: vacancy title, department, applied date, current pipeline stage (as a visual step indicator), and status.
- FR-3: The portal SHALL display upcoming interview details: date, time, duration, type, location/video link, and interviewer name(s).
- FR-4: The portal SHALL allow the applicant to download their submitted resume and any offer letter PDFs (via signed, short-lived blob storage URLs).
- FR-5: The portal SHALL provide "Accept" and "Decline" buttons for active offers, with a confirmation dialog before submission.
- FR-6: The portal SHALL display a timeline/activity log of status changes for each application (e.g., "Application received", "Moved to Screening", "Interview scheduled").
- FR-7: The system SHALL send email notifications to the applicant when their status changes, an interview is scheduled, or an offer is sent, with a link back to the portal.
- FR-8: The portal magic link SHALL expire after a configurable period (default 30 days) and be regenerable by the applicant via email verification.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: The candidate portal SHALL load within 2.5 seconds (P95) on a 4G connection.
- NFR-2: The portal SHALL be fully responsive (360px to 4K) and accessible (WCAG 2.1 AA).
- NFR-3: All data displayed on the portal SHALL be tenant-scoped; the portal resolves tenant context from the subdomain.
- NFR-4: Magic link tokens SHALL be cryptographically signed (HMAC-SHA256) and include the tenant_id, applicant email, and expiry timestamp.
- NFR-5: The portal SHALL not expose sensitive internal data (rejection reasons, interviewer comments, scorecard details) to the applicant.
- NFR-6: Rate limiting SHALL be applied to magic link generation endpoints to prevent abuse.

## 6. Business Rules
- BR-1: The candidate portal is read-only for application status; applicants cannot edit their submitted application.
- BR-2: Offer acceptance/decline through the portal is a one-time action; once submitted, it cannot be changed by the applicant (the recruiter can override if needed).
- BR-3: The portal shows a sanitized view of the pipeline: applicants see stage names but not internal notes, scorecards, or rejection reasons.
- BR-4: The portal is only accessible for applications within the tenant resolved from the subdomain (e.g., `acme.yourhrm.com`).
- BR-5: If the magic link has expired, the applicant can request a new one by entering their email; the system sends a fresh link after verifying the email matches an existing application.
- BR-6: The portal does not require a traditional user account; access is controlled via magic links tied to the applicant's email.

## 7. Data Requirements
- **Input:** Magic link token (containing encrypted applicant email + tenant_id + expiry), applicant email for link regeneration, offer response (accept/decline).
- **Output:** Application dashboard DTO: vacancy title, department, applied date, current stage, interview details, offer details (sanitized), timeline events.
- **Storage:** `applicant_portal_token` table: `id`, `tenant_id`, `applicant_email`, `token_hash`, `expires_at`, `created_at`. Existing `applicant`, `interview`, `offer` tables for data retrieval.

## 8. UI/UX Notes
- **Dashboard layout:** Clean, centered single-column layout with application cards. Each card shows the vacancy title and a horizontal step indicator showing progress through pipeline stages (completed stages in green, current in blue, future in gray).
- **Interview section:** Card with calendar icon, date/time prominently displayed, type badge, and a map link for in-person or "Join Meeting" button for video interviews.
- **Offer section:** Highlighted card with offer details summary, PDF download button, and prominent "Accept" / "Decline" buttons with a confirmation modal.
- **Timeline:** Vertical timeline (Notion-like) showing application events in chronological order.
- **Tenant branding:** Portal uses the tenant's logo and primary color from branding configuration.
- **Mobile:** Full-width cards, large touch targets, swipeable step indicator.
- **Empty state:** "No applications found" with a link to the careers page.
- **Loading state:** Skeleton loaders for cards while data loads.

## 9. Dependencies
- US-REC-002 (application must exist).
- US-REC-005 (interview details to display).
- US-REC-007 (offer details and acceptance/decline actions).
- Notification System (S25) for magic link emails and status update notifications.
- File & Document Management (S26) for resume and offer letter PDF downloads (signed URLs).
- Tenant branding configuration (S35.2.5).

## 10. Assumptions & Constraints
- The candidate portal is a lightweight, publicly accessible (via magic link) section of the tenant's subdomain, not a separate application.
- No traditional account creation or password is required for applicants; magic links serve as the authentication mechanism.
- The portal does not support real-time updates (polling or manual refresh); real-time via SignalR is a future enhancement.
- The portal is scoped to one tenant per subdomain; if the applicant has applied to multiple tenants, they need separate portal links per tenant.
- The portal does not expose any PII of other applicants or internal users beyond interviewer names.

## 11. Test Hints
- Generate a magic link and verify it grants access to the correct applicant's data within the correct tenant.
- Test magic link expiry: access the portal after the token expires and verify access is denied with a "request new link" prompt.
- Test offer accept/decline: accept an offer and verify the offer status updates and the applicant advances to "Hired".
- Verify the portal does not expose rejection reasons, scorecard data, or internal notes.
- Test cross-tenant isolation: use a Tenant A magic link on Tenant B's subdomain and verify access is denied.
- Test mobile layout at 360px: verify step indicator, interview card, and offer buttons are usable.
- Test accessibility: run WCAG 2.1 AA automated checks on the portal pages.
- Test rate limiting: send 20 magic link regeneration requests in rapid succession and verify throttling kicks in.
- Verify tenant branding (logo, primary color) is applied to the portal.
