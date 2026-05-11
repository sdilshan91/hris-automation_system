---
id: US-PRF-002
module: Performance Management
priority: Must Have
persona: Employee
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-PRF-002: Employee Self-Rates Against Goals

## 1. Description
**As an** Employee,
**I want to** self-rate my performance against each assigned goal/KPI during the review period,
**So that** I can provide my perspective on my achievements before my manager completes their assessment, ensuring a balanced and fair evaluation.

## 2. Preconditions
- The employee is authenticated and has the `Performance.Read.Self` permission.
- Goals have been assigned and acknowledged for the current appraisal cycle (US-PRF-001).
- The self-assessment window is open for the active appraisal cycle.
- The Performance module is enabled for the tenant.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | The self-assessment window is open and goals exist for the employee | The employee navigates to Performance > My Review | The system displays all assigned goals with fields for self-rating (configurable scale, e.g., 1-5), achievement percentage, and self-assessment comments |
| AC-2 | The employee has filled in self-ratings for all goals | The employee clicks "Submit Self-Assessment" | The system saves the self-assessment, changes the status to "Self-Assessment Submitted", notifies the manager, and prevents further edits unless reopened |
| AC-3 | The employee has partially completed the self-assessment | The employee clicks "Save as Draft" | The system saves progress without submitting, allowing the employee to return and complete later |
| AC-4 | The self-assessment window has closed | The employee attempts to submit or edit self-ratings | The system displays a read-only view with a message: "The self-assessment period for this cycle has ended" |
| AC-5 | The employee has not completed self-assessment and the deadline is approaching | The system reaches the reminder threshold (e.g., 3 days before deadline) | Hangfire triggers an automated reminder notification (in-app + email) to the employee |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system shall display each goal with its title, description, weight, target value, and due date alongside self-rating input fields.
- FR-2: Self-rating shall use the tenant-configured rating scale (e.g., 1-5 stars, 1-10 numeric, or descriptive labels like Exceeds/Meets/Below).
- FR-3: The system shall require a self-assessment comment (minimum 20 characters) for each goal before submission.
- FR-4: The system shall calculate a weighted self-assessment score based on individual goal ratings and their weights.
- FR-5: The system shall support file attachments (evidence/artifacts) per goal, limited to 5 files, max 10MB each.
- FR-6: The system shall allow save-as-draft functionality, persisting partial self-assessments.
- FR-7: Hangfire shall schedule reminder jobs for employees who have not submitted self-assessments as the deadline approaches.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Self-assessment form shall load within 400ms (P95) including all goal data.
- NFR-2: All self-assessment data shall be tenant-isolated via PostgreSQL RLS; an employee shall only see their own assessment data.
- NFR-3: Draft auto-save shall occur every 60 seconds to prevent data loss.
- NFR-4: File uploads shall be virus-scanned before acceptance and stored in tenant-scoped storage paths.
- NFR-5: The UI shall be fully responsive and operable on mobile devices (360px+), supporting touch interactions for rating inputs.

## 6. Business Rules
- BR-1: Self-assessment can only be submitted during the self-assessment phase window of the active cycle.
- BR-2: All goals must be rated before submission; partial submissions are saved as drafts only.
- BR-3: Once submitted, the self-assessment is locked unless the manager or HR explicitly reopens it.
- BR-4: The self vs. manager rating weight ratio (e.g., 30:70) is tenant-configurable and applied during final score calculation.
- BR-5: Self-assessment is optional if the tenant has disabled it in Performance module configuration; in that case, only manager rating applies.

## 7. Data Requirements
- **Input:** goal ID, self-rating (integer/decimal per scale), achievement percentage (0-100), self-assessment comment (text), optional file attachments.
- **Output:** self-assessment record with status (draft/submitted), weighted self-score, submission timestamp, tenant_id.
- **Storage:** Self-assessment table with foreign keys to goal and cycle, tenant_id column with RLS policy.

## 8. UI/UX Notes
- Notion-like clean layout with each goal as an expandable card.
- Star rating or slider component for self-rating input (Angular Material).
- Progress indicator showing how many goals have been rated out of total.
- Rich text editor for self-assessment comments.
- Drag-and-drop file upload area for evidence attachments.
- Mobile: collapsible accordion for goals, sticky submit button at bottom.

## 9. Dependencies
- US-PRF-001: Goals must be assigned and acknowledged before self-assessment.
- US-PRF-004: Appraisal cycle with self-assessment window must be configured.
- Notification system for deadline reminders (Hangfire background job).
- File management module for evidence attachments.

## 10. Assumptions & Constraints
- The rating scale is configured at the tenant level and consistent across all goals in a cycle.
- Hangfire reminder schedules are configurable by HR (default: 7 days, 3 days, 1 day before deadline).
- File storage uses the platform's existing document management infrastructure with tenant-scoped paths.
- Self-assessment comments are stored as plain text or sanitized HTML.

## 11. Test Hints
- Verify tenant isolation: ensure Employee A in Tenant X cannot view Employee B's self-assessment in Tenant X or any data in Tenant Y.
- Test draft save and resume: save partial assessment, log out, log back in, confirm data persists.
- Test auto-save: fill in ratings, wait 60 seconds, simulate browser crash, confirm data recovered.
- Test submission validation: attempt to submit with one goal unrated, confirm error.
- Test window enforcement: attempt to submit after the self-assessment window closes.
- Test Hangfire reminders: configure a deadline, advance time, verify reminder notifications fire.
- Test file upload: upload files of various types and sizes, verify virus scan and tenant-scoped storage.
- Test mobile at 360px: confirm all rating inputs are usable with touch.
