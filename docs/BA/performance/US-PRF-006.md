---
id: US-PRF-006
module: Performance Management
priority: Should Have
persona: Manager
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 4
---

# US-PRF-006: Performance Review Meeting Notes and Sign-Off

## 1. Description
**As a** Manager,
**I want to** document performance review meeting notes and obtain digital sign-off from both myself and the employee,
**So that** there is a formal, auditable record of the review discussion, agreed-upon development actions, and mutual acknowledgement of the assessment outcome.

## 2. Preconditions
- The manager is authenticated and has the `Performance.Review.Team` permission.
- The manager review has been submitted for the employee in the current cycle (US-PRF-003).
- The appraisal cycle is in the review meeting or sign-off phase.
- The Performance module is enabled for the tenant.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The manager review is submitted and the sign-off phase is active | The manager navigates to the employee's review and clicks "Add Meeting Notes" | The system displays a rich text editor pre-populated with a template containing sections: strengths, areas for improvement, agreed development actions, and overall discussion summary |
| AC-2 | The manager has completed meeting notes | The manager clicks "Request Employee Sign-Off" | The system saves the meeting notes, changes the review status to "Pending Employee Sign-Off", and sends a notification to the employee with a link to review and sign |
| AC-3 | The employee receives the sign-off request and reviews the notes | The employee clicks "Acknowledge & Sign" or "Dispute" | If acknowledged: the system records a digital signature (name + timestamp), finalizes the review status to "Signed Off". If disputed: the system captures the employee's dispute comments and notifies the manager and HR |
| AC-4 | Both manager and employee have signed off | HR or the manager views the completed review | The system displays the full review record with goals, ratings, meeting notes, sign-off timestamps, and digital signatures, with an option to export as PDF |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system shall provide a rich text editor for meeting notes with a configurable template (tenant-level).
- FR-2: Meeting notes shall support sections: key strengths, development areas, agreed actions with deadlines, and overall summary.
- FR-3: The system shall implement a digital sign-off workflow: manager signs first, then the employee is requested to sign.
- FR-4: Employee sign-off shall offer two options: "Acknowledge & Sign" (agreement) or "Dispute" (disagreement with mandatory comments).
- FR-5: Disputed reviews shall be escalated to HR with the employee's dispute comments for resolution.
- FR-6: The system shall generate a PDF of the complete review (goals, ratings, notes, signatures) using tenant branding.
- FR-7: All sign-off actions shall be immutably audit-logged with user ID, timestamp, and IP address.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Meeting notes editor shall load within 400ms (P95).
- NFR-2: All meeting notes and sign-off data shall be tenant-isolated via PostgreSQL RLS.
- NFR-3: Digital sign-off records shall be immutable once recorded; no user (including HR) can modify a recorded signature.
- NFR-4: PDF export shall complete within 3 seconds for a single review document.
- NFR-5: The sign-off workflow shall be accessible on mobile devices with touch-friendly confirmation dialogs.

## 6. Business Rules
- BR-1: Meeting notes can only be added after the manager review is submitted.
- BR-2: The employee must review the notes before signing; the system tracks whether the employee has opened/read the notes.
- BR-3: If an employee does not sign off within a configurable period (default: 7 days), the system auto-closes the review with a "No Response" status and notifies HR.
- BR-4: Disputed reviews remain in "Disputed" status until HR resolves them by either amending the review or confirming it.
- BR-5: Once both parties sign off, the review is locked and cannot be edited by anyone except system admin for compliance corrections.

## 7. Data Requirements
- **Input:** meeting notes (rich text), manager sign-off (name, timestamp, IP), employee sign-off or dispute (name, timestamp, IP, dispute comments).
- **Output:** signed review record with complete audit trail, exportable PDF.
- **Storage:** review_meeting_notes table, review_signoffs table (immutable append-only), tenant_id with RLS policy.

## 8. UI/UX Notes
- Notion-like rich text editor for meeting notes with markdown support.
- Meeting notes template auto-populated with goal titles and ratings for reference.
- Sign-off confirmation modal with clear language: "By signing, you acknowledge this review has been discussed."
- Dispute flow: text area for employee comments with a "Submit Dispute" button.
- PDF preview before download with tenant logo and branding.
- Status timeline showing: Notes Added -> Sign-Off Requested -> Employee Signed / Disputed -> Completed.
- Mobile: full-screen editor for notes, large touch targets for sign-off buttons.

## 9. Dependencies
- US-PRF-003: Manager review must be submitted before meeting notes.
- US-PRF-004: Appraisal cycle must include a sign-off phase.
- Notification system for sign-off requests and dispute alerts.
- PDF generation library (free, open-source).
- Audit logging module for immutable sign-off records.

## 10. Assumptions & Constraints
- Digital sign-off constitutes an electronic acknowledgement, not a legally binding signature (unless tenant configures otherwise).
- Meeting notes templates are configured by HR at the tenant level.
- The auto-close timeout for unsigned reviews is tenant-configurable via Performance module settings.
- Rich text is stored as sanitized HTML to prevent XSS.

## 11. Test Hints
- Verify tenant isolation: meeting notes in Tenant A are not accessible from Tenant B.
- Test sign-off workflow: manager signs, employee receives notification, employee signs, review locks.
- Test dispute flow: employee disputes, HR receives notification, HR resolves, review status updates.
- Test auto-close: set timeout to a short period, verify review auto-closes with "No Response" status.
- Test immutability: attempt to modify a recorded sign-off via API, confirm rejection.
- Test PDF export: verify tenant branding, all sections present, signatures displayed.
- Test mobile sign-off: complete the sign-off flow on a 360px viewport.
- Test audit trail: verify all sign-off actions appear in the tenant audit log with correct timestamps.
