---
id: US-CHR-009
module: Core HR
priority: Must Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-CHR-009: Employee Status Management (Active, Probation, Suspended, Terminated)

## 1. Description
**As an** HR Officer,
**I want to** manage employee lifecycle statuses (active, probation, suspended, terminated),
**So that** the system accurately reflects each employee's current standing and downstream modules (leave, attendance, payroll, portal access) behave correctly based on status.

## 2. Preconditions
- The user is authenticated with HR Officer or Tenant Admin role within their tenant.
- The employee record exists (see US-CHR-001).

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An HR Officer views an employee profile with status "active" | They click "Change Status" | A status transition form appears showing available transitions based on the current status (e.g., active -> suspended, active -> terminated, active -> probation). Invalid transitions are not shown. |
| AC-2 | An HR Officer changes an employee's status from "active" to "suspended" with a reason and effective date | They confirm the change | The employee's status is updated; the status change is recorded in the employment history timeline with reason, effective date, and the officer who made the change; an audit log entry is created. |
| AC-3 | An employee's status is changed to "terminated" | The change is saved | The system deactivates the employee's user login (if linked), removes them from active headcount reports, excludes them from future payroll runs, and disables their self-service portal access. Their data is retained per the tenant's retention policy. |
| AC-4 | An employee is in "probation" status and the probation end date arrives | The daily background job runs | The system sends a notification to the HR Officer reminding them to confirm the employee (transition to "active") or extend probation. It does NOT auto-transition. |
| AC-5 | An HR Officer attempts an invalid status transition (e.g., "terminated" -> "probation") | They try to change status | The system rejects the transition with: "Invalid status transition. Terminated employees cannot be moved to probation." Only valid transitions are permitted. |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system SHALL support the following employee statuses: `active`, `probation`, `suspended`, `terminated`, `inactive`.
- FR-2: The system SHALL enforce a valid state machine for status transitions:
  - `probation` -> `active`, `terminated`
  - `active` -> `suspended`, `terminated`, `inactive`
  - `suspended` -> `active`, `terminated`
  - `inactive` -> `active`, `terminated`
  - `terminated` -> (no outbound transitions; terminal state)
- FR-3: Every status change SHALL require a reason (text field) and an effective date.
- FR-4: The system SHALL record all status changes in an employment history / status change log with timestamp, actor, reason, and effective date.
- FR-5: The system SHALL trigger side effects based on the new status:
  - `suspended`: disable portal access, pause leave accrual
  - `terminated`: disable portal access, exclude from payroll, trigger offboarding workflow (if configured)
  - `active`: enable portal access, resume leave accrual
- FR-6: The system SHALL run a daily background job to check for probation end dates approaching within 7 days and send HR notifications.
- FR-7: The system SHALL display the current status as a color-coded badge on the employee profile and directory.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Status change API response time SHALL be <= 800 ms (P95).
- NFR-2: All status data SHALL be tenant-isolated via RLS and EF Core global query filters.
- NFR-3: Status changes SHALL be idempotent (using `Idempotency-Key` header) to prevent duplicate transitions from network retries.
- NFR-4: The status change UI SHALL be fully responsive (360px to 4K).
- NFR-5: Status change operations SHALL be fully audited with before/after snapshots.

## 6. Business Rules
- BR-1: The status state machine is enforced server-side; the UI only presents valid transitions.
- BR-2: Only HR Officers and Tenant Admins can change employee status; Managers and Employees cannot.
- BR-3: "Terminated" is a terminal state. If a terminated employee is rehired, a new employee record is created.
- BR-4: Status changes with a future effective date are stored but not applied until that date (via a background job or on-access check).
- BR-5: Suspended employees are excluded from active headcount but their records are fully retained.
- BR-6: Probation periods are configured per tenant (default 90 days from date_of_joining).

## 7. Data Requirements
**Status change log / Employment history entry:**
| Column | Type | Required | Notes |
|--------|------|----------|-------|
| history_id | uuid (PK) | Auto | |
| tenant_id | uuid (FK) | Auto | |
| employee_id | uuid (FK) | Yes | |
| change_type | varchar(50) | Yes | "status_change" |
| previous_value | varchar(50) | Yes | Previous status |
| new_value | varchar(50) | Yes | New status |
| reason | text | Yes | |
| effective_date | date | Yes | |
| changed_by | uuid | Yes | |
| changed_at | timestamptz | Auto | |

**Status badge color mapping:**
| Status | Color | Tailwind Class |
|--------|-------|----------------|
| active | Green | `bg-green-100 text-green-800` |
| probation | Amber | `bg-amber-100 text-amber-800` |
| suspended | Gray | `bg-gray-100 text-gray-800` |
| terminated | Red | `bg-red-100 text-red-800` |
| inactive | Slate | `bg-slate-100 text-slate-800` |

## 8. UI/UX Notes (Notion-like, cards-based)
- Status badge on the employee profile header card: pill-shaped, color-coded per the mapping above.
- "Change Status" button appears next to the status badge (visible only to HR Officers).
- Status change form: modal card with fields for New Status (dropdown showing only valid transitions), Effective Date (date picker), Reason (textarea, required).
- Confirmation step: "Are you sure you want to change [Employee Name]'s status from [Current] to [New]? This will [list side effects]." with Cancel/Confirm buttons.
- Employment history section: vertical timeline showing all status changes with date, new status badge, reason, and who made the change.
- On mobile: modal becomes a bottom sheet; timeline is a compact card list.
- Smooth transitions: badge color change animates (200ms ease).

## 9. Dependencies
- US-CHR-001: Employee must exist.
- US-CHR-002: Status is displayed and managed on the employee profile.
- Authentication module: Portal access is enabled/disabled based on status.
- Leave module (future): Accrual pauses on suspension.
- Payroll module (future): Terminated employees excluded from runs.
- Notification System (Technical Doc S25): Probation end reminders.
- Background Jobs (Technical Doc S28): Probation check job, future-dated status application.

## 10. Assumptions & Constraints
- The status state machine is hardcoded in the application; tenants cannot customize transitions in Phase 1.
- Future-dated status changes require a daily background job to apply them on the effective date.
- Rehiring a terminated employee creates a new record rather than reactivating the old one (per BR-3).
- Only free/open-source libraries are used.

## 11. Test Hints
- **Valid transition:** Change active -> suspended; verify status updates, audit log created, portal access disabled.
- **Invalid transition:** Attempt terminated -> probation via API; expect 400 with error message.
- **Side effects:** Terminate an employee; verify user login is disabled and employee excluded from headcount queries.
- **Probation reminder:** Set probation end date to 5 days from now; run background job; verify notification sent to HR.
- **Tenant isolation:** Change status in Tenant A; verify no impact on Tenant B data.
- **Idempotency:** Send the same status change request with the same Idempotency-Key twice; verify only one change is recorded.
- **Future effective date:** Set effective date to tomorrow; verify status is unchanged today; run background job; verify it changes.
- **Employment history:** Make 3 status changes; verify 3 timeline entries with correct data.
