---
id: US-REC-004
module: Recruitment
priority: Must Have
persona: Recruiter
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-REC-004: Move Applicant Through Pipeline Stages (Screening to Interview to Offer)

## 1. Description
**As a** Recruiter,
**I want to** advance applicants through the recruitment pipeline stages -- from screening through interview to offer -- with required actions at each gate,
**So that** the hiring process follows a structured workflow and every stage transition is tracked and auditable.

## 2. Preconditions
- The user is authenticated and has `Recruitment.Manage.All` permission within the resolved tenant.
- The applicant exists in the tenant's recruitment pipeline and is not in a terminal stage (Hired or Rejected).
- Pipeline stages and their gate requirements are configured for the tenant.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An applicant is in the "Applied" stage | The recruiter reviews the application and clicks "Move to Screening" | The applicant's stage updates to "Screening", a stage transition record is created with timestamp and user, and the applicant receives an email notification (if configured) |
| AC-2 | An applicant is in the "Screening" stage | The recruiter marks screening as passed and moves the applicant to "Interview" | The system validates that screening notes/decision have been recorded, updates the stage to "Interview", and prompts the recruiter to schedule an interview |
| AC-3 | An applicant has completed all interviews with scores submitted | The recruiter moves the applicant to "Offer" | The system validates that at least one interview scorecard exists, updates the stage to "Offer", and triggers the offer generation workflow (US-REC-007) |
| AC-4 | An applicant is in any active stage | The recruiter clicks "Reject" | A modal prompts for rejection reason (required dropdown: not qualified, position filled, withdrew, other) and optional notes; upon confirmation, the applicant moves to "Rejected" and receives a configurable rejection email |
| AC-5 | A stage transition occurs in Tenant A | The audit log is queried | The transition is recorded with tenant_id, applicant_id, from_stage, to_stage, changed_by, timestamp, and notes; no cross-tenant audit entries are visible |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL enforce stage transition rules: each stage MAY define gate criteria that must be met before advancing (e.g., "Interview" stage requires at least one scheduled interview; "Offer" stage requires at least one completed interview scorecard).
- FR-2: The system SHALL support the following stage transitions: Applied -> Screening -> Interview -> Offer -> Hired, with the ability to skip stages if permitted by tenant configuration.
- FR-3: The system SHALL allow rejection from any active stage, requiring a rejection reason and optional notes.
- FR-4: The system SHALL record every stage transition in the `applicant_stage_history` table with: applicant_id, from_stage_id, to_stage_id, changed_by_user_id, changed_at timestamp, notes, and tenant_id.
- FR-5: The system SHALL support backward stage movement (e.g., moving from "Interview" back to "Screening") only for users with `Recruitment.Manage.All` permission, requiring a mandatory reason.
- FR-6: The system SHALL send configurable email notifications to the applicant at each stage transition (templates managed via tenant notification settings).
- FR-7: The system SHALL update pipeline stage counts on the Kanban board in real-time (optimistic UI update with server confirmation).
- FR-8: The system SHALL prevent stage advancement if the vacancy has been closed or cancelled.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Stage transition API calls SHALL complete within 800 ms (P95), including audit log persistence.
- NFR-2: All stage transition data SHALL be tenant-scoped with `tenant_id` and protected by PostgreSQL RLS.
- NFR-3: Stage transition and audit log writes SHALL be atomic (single database transaction).
- NFR-4: The stage transition UI SHALL provide immediate visual feedback (optimistic update) while the server persists the change.
- NFR-5: Email notifications for stage transitions SHALL be queued via Hangfire for async delivery, not blocking the API response.

## 6. Business Rules
- BR-1: Gate criteria are configurable per tenant per stage; defaults are: Screening requires screening notes, Interview requires at least one scheduled interview, Offer requires at least one completed scorecard.
- BR-2: A rejected applicant cannot be advanced to further stages unless they are first moved back to an active stage by a user with `Recruitment.Manage.All` permission (reactivation).
- BR-3: The "Hired" stage is terminal and irreversible; it triggers the applicant-to-employee conversion workflow.
- BR-4: If the vacancy headcount has been fully filled (all positions have hired applicants), the system SHALL warn before allowing more applicants to reach the "Offer" or "Hired" stage.
- BR-5: Stage transition emails use the tenant's configured notification templates with variable substitution (applicant name, vacancy title, stage name, company name).
- BR-6: Bulk stage transitions (moving multiple applicants at once) apply the same gate criteria to each applicant individually; failures are reported per applicant.

## 7. Data Requirements
- **Input:** Applicant ID, target stage ID, transition notes (optional for forward moves, required for backward moves and rejections), rejection reason (required for rejection).
- **Output:** Updated applicant record with new `pipeline_stage_id`, new `applicant_stage_history` record, notification outbox entry.
- **Storage:** `applicant_stage_history` table: `id` (UUID), `tenant_id`, `applicant_id`, `from_stage_id`, `to_stage_id`, `changed_by`, `changed_at`, `notes`, `rejection_reason`. RLS policy on `tenant_id`.

## 8. UI/UX Notes
- Stage transitions can be triggered via Kanban drag-and-drop (US-REC-003) or via action buttons on the applicant detail panel.
- When moving to a gated stage, show a confirmation dialog listing the gate criteria and their pass/fail status (green checkmarks / red crosses).
- Rejection flow: modal dialog with required reason dropdown, optional notes textarea, and a preview of the rejection email (if notification is enabled).
- Stage transition timeline on the applicant detail panel: vertical timeline with stage names, dates, user who made the change, and notes -- Notion-like minimal design.
- Visual indicators on applicant cards: time-in-stage badge (e.g., "In Screening for 5 days") to highlight bottlenecks.
- Mobile: action buttons in the applicant detail panel replace drag-and-drop for stage transitions.

## 9. Dependencies
- US-REC-001 (vacancy with configured pipeline stages).
- US-REC-002 (applicants in the pipeline).
- US-REC-003 (Kanban board for drag-and-drop transitions).
- US-REC-005 (interview scheduling for Interview stage gate).
- US-REC-006 (interview scorecards for Offer stage gate).
- US-REC-007 (offer generation triggered by Offer stage).
- Notification System (S25) for stage transition emails.
- Hangfire for async email delivery.
- Audit Logging module.

## 10. Assumptions & Constraints
- Stage gate criteria are "soft gates" in Phase 1: they warn the user but can be overridden by users with `Recruitment.Manage.All` permission.
- The notification templates for each stage transition are pre-seeded with defaults but customizable by the tenant admin.
- Concurrent stage transitions on the same applicant are handled with optimistic concurrency (EF Core concurrency token on the applicant record).
- The approval workflow engine (S34) is not used for recruitment stage transitions in Phase 1; transitions are direct actions by authorized users.

## 11. Test Hints
- Test the full happy path: Applied -> Screening -> Interview -> Offer -> Hired; verify each transition creates a history record.
- Test gate enforcement: attempt to move to "Offer" without any interview scorecard; verify the gate blocks (or warns).
- Test rejection from each stage; verify rejection reason is required and stored.
- Test backward movement: move from "Interview" to "Screening"; verify reason is required and `Recruitment.Manage.All` permission is checked.
- Test vacancy headcount warning: fill all positions and attempt to hire another applicant.
- Test concurrent update: two users move the same applicant simultaneously; verify optimistic concurrency handles the conflict.
- Test cross-tenant isolation on `applicant_stage_history`: verify Tenant B cannot see Tenant A's transition records.
- Verify email notifications are queued in the notification outbox and processed by Hangfire.
