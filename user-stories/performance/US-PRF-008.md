---
id: US-PRF-008
module: Performance Management
priority: Should Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-PRF-008: Performance Improvement Plan (PIP)

## 1. Description
**As an** HR Officer,
**I want to** create and manage Performance Improvement Plans (PIPs) for underperforming employees with defined milestones, timelines, and review checkpoints,
**So that** the organization provides structured support to employees who need improvement, maintains documentation for compliance, and ensures fair, transparent corrective action processes.

## 2. Preconditions
- The HR Officer is authenticated and has `Performance.Review.All` permission.
- The employee has a completed performance review with a rating below the tenant-configured PIP threshold.
- The employee's manager has flagged the employee for performance improvement (US-PRF-003, FR-6) or HR has independently initiated the PIP.
- The Performance module is enabled for the tenant.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An employee has been flagged for performance improvement | The HR Officer navigates to Performance > PIP and clicks "Create PIP" | The system displays a PIP creation form with fields: employee name (pre-filled), PIP reason, duration (start/end dates), improvement objectives (title, description, success criteria, due date), review checkpoint dates, assigned mentor/coach, and escalation action if PIP is not met |
| AC-2 | The HR Officer has completed the PIP form | The HR Officer clicks "Initiate PIP" | The system creates the PIP, notifies the employee, their manager, and the assigned mentor via in-app and email, and schedules Hangfire jobs for checkpoint reminders |
| AC-3 | A PIP checkpoint date arrives | The manager or HR navigates to the PIP and clicks "Record Checkpoint" | The system displays a checkpoint form where the reviewer records: progress assessment, evidence of improvement, updated status (on track, at risk, not met), and comments; the employee is notified of the checkpoint outcome |
| AC-4 | The PIP duration ends and all checkpoints have been recorded | HR reviews the PIP outcome | The system presents a summary of all checkpoints and allows HR to set the final PIP outcome: "Successfully Completed" (PIP closed, employee returns to normal status), "Extended" (new end date set), or "Not Met" (triggers configured escalation action) |
| AC-5 | The PIP outcome is "Not Met" and escalation is configured | HR confirms the escalation action | The system records the escalation decision (e.g., reassignment, demotion, termination recommendation), notifies relevant stakeholders, and creates an immutable audit record |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system shall support creating PIPs with: reason, duration (30/60/90 days configurable), improvement objectives with measurable success criteria, checkpoint schedule, mentor assignment, and escalation rules.
- FR-2: Each PIP shall have a status lifecycle: Draft, Active, Extended, Successfully Completed, Not Met, Cancelled.
- FR-3: The system shall schedule Hangfire jobs for: PIP start notification, checkpoint reminders (3 days before each checkpoint), PIP end date reminder, and overdue checkpoint alerts.
- FR-4: The system shall allow recording checkpoint assessments with progress status, evidence notes, and file attachments.
- FR-5: The system shall maintain a complete, immutable history of all PIP actions, status changes, and checkpoint outcomes for compliance and legal purposes.
- FR-6: The system shall support PIP extension with a new end date and additional objectives if needed.
- FR-7: The system shall generate a PIP summary report (PDF) including all objectives, checkpoints, outcomes, and signatures.
- FR-8: The system shall restrict PIP visibility: only the employee, their manager, HR, and the assigned mentor can view the PIP.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: PIP creation and checkpoint recording shall complete within 800ms (P95).
- NFR-2: All PIP data shall be tenant-isolated via PostgreSQL RLS.
- NFR-3: PIP records shall be retained for the tenant-configured retention period (default: 7 years) for compliance.
- NFR-4: PIP data shall be encrypted at rest using pgcrypto for sensitive fields (reason, escalation notes) given its sensitive HR nature.
- NFR-5: The PIP UI shall be accessible on mobile devices for managers to record checkpoint notes during in-person meetings.

## 6. Business Rules
- BR-1: Only HR Officers with `Performance.Review.All` can create, extend, or close PIPs; managers can record checkpoints and add comments but cannot unilaterally close a PIP.
- BR-2: An employee can have only one active PIP at a time.
- BR-3: PIP duration must be a minimum of 30 days.
- BR-4: The employee must acknowledge PIP initiation (similar to review sign-off); if not acknowledged within 5 business days, it proceeds with a "Not Acknowledged" flag.
- BR-5: PIP data is excluded from general performance dashboards and reports; it is only visible in the dedicated PIP management section.
- BR-6: Escalation actions are configurable per tenant: options include reassignment, demotion, contract non-renewal, or termination recommendation.

## 7. Data Requirements
- **Input:** employee_id, PIP reason (text), start date, end date, improvement objectives (title, description, success criteria, due date), checkpoint dates, mentor_id, escalation action type.
- **Output:** PIP record with status, checkpoint history, outcome, audit trail, tenant_id.
- **Storage:** pip table, pip_objectives table, pip_checkpoints table, all with tenant_id and RLS policies. Sensitive fields encrypted with pgcrypto.

## 8. UI/UX Notes
- Clean, professional form layout for PIP creation (Notion-like card sections).
- Timeline view showing PIP duration with checkpoint markers.
- Checkpoint recording as a modal overlay with status selector (traffic light: green/amber/red).
- PIP summary view with accordion sections for each objective and its checkpoints.
- Sensitive nature indicator: subtle banner reminding users that PIP data is confidential.
- Mobile: simplified timeline, full-screen checkpoint form for on-the-go recording.

## 9. Dependencies
- US-PRF-003: Manager review with performance improvement flag.
- US-PRF-007: Performance dashboard (PIP data is excluded from general analytics).
- Core HR: employee records, org tree for manager/mentor relationships.
- Notification system and Hangfire for checkpoint reminders.
- Audit logging for compliance trail.
- File management for evidence attachments.

## 10. Assumptions & Constraints
- PIP processes vary by tenant; the system provides a flexible framework rather than enforcing a single process.
- Legal review of PIP templates is the tenant's responsibility; the system provides the tooling.
- PIP data retention follows the tenant's configured data retention policy.
- Encryption of sensitive PIP fields uses pgcrypto, consistent with the platform's PII encryption strategy.

## 11. Test Hints
- Verify tenant isolation: PIP in Tenant A is invisible from Tenant B.
- Verify access control: only the employee, manager, HR, and mentor can view the PIP; other employees cannot.
- Test PIP lifecycle: Draft -> Active -> checkpoint recorded -> Extended -> Successfully Completed.
- Test PIP lifecycle (negative): Draft -> Active -> checkpoints show "Not Met" -> escalation triggered.
- Test Hangfire reminders: create a PIP, verify checkpoint reminders fire at configured intervals.
- Test employee acknowledgement: initiate PIP, verify employee notification, test acknowledgement and non-acknowledgement flows.
- Test concurrent PIP prevention: attempt to create a second PIP for an employee with an active PIP.
- Test data encryption: query the database directly, verify PIP reason and escalation notes are encrypted.
- Test PDF report generation: verify all sections, checkpoints, and signatures are included.
