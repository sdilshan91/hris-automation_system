---
id: US-ATT-005
module: Attendance
priority: Must Have
persona: HR Officer
status: draft
created: 2026-05-11
sprint: backlog
acceptance_criteria_count: 5
---

# US-ATT-005: Shift Management and Assignment per Employee

## 1. Description
**As an** HR Officer,
**I want to** create and manage shift definitions and assign shifts to employees,
**So that** the system can accurately determine expected work hours, late arrivals, and overtime for each employee.

## 2. Preconditions
- HR Officer must be authenticated with a valid JWT session.
- HR Officer must have the `Attendance.*.All` permission.
- The Attendance module must be enabled for the tenant.
- Employee records must exist in the Core HR module.

## 3. Acceptance Criteria (IEEE 830 S3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | HR Officer navigates to shift management | HR Officer creates a new shift with name, start time, end time, break duration, and working days | The shift is saved in the `shift` table with the `tenant_id` and is available for assignment |
| AC-2 | A shift definition exists | HR Officer assigns the shift to one or more employees with an effective date | `employee_shift` records are created linking each employee to the shift with the effective date |
| AC-3 | An employee already has an active shift assignment | HR Officer assigns a new shift to the employee with a future effective date | The current shift remains active until the new shift's effective date; the system does not create overlapping active assignments |
| AC-4 | HR Officer attempts to delete a shift that is currently assigned to employees | HR Officer clicks "Delete" | The system prevents deletion and displays: "This shift is assigned to {N} employees. Please reassign them before deleting." |
| AC-5 | HR Officer creates a rotating shift schedule | HR Officer defines a rotation pattern (e.g., Week A: Morning, Week B: Evening) and assigns it to employees | The system creates the rotation pattern and automatically determines the applicable shift for each day based on the rotation cycle |

## 4. Functional Requirements (IEEE 830 S3.2)
- FR-1: The system shall support three shift types: Single (fixed), Rotating (cyclic pattern), and Flexible (no fixed start/end, only total hours required).
- FR-2: The system shall allow HR to define shift parameters: name, type, start_time, end_time, break_duration_minutes, grace_period_minutes, minimum_hours, working_days (bitmask or array for Mon-Sun).
- FR-3: The system shall allow bulk assignment of a shift to multiple employees at once.
- FR-4: The system shall maintain shift assignment history via effective_from and effective_to dates on `employee_shift`.
- FR-5: The system shall provide a default shift per tenant for employees without explicit assignments.
- FR-6: The system shall prevent deletion of shifts that have active employee assignments.
- FR-7: For rotating shifts, the system shall store the rotation pattern and calculate the applicable shift for any given date.
- FR-8: The system shall allow HR to clone an existing shift definition to create a variant.

## 5. Non-Functional Requirements (IEEE 830 S3.3)
- NFR-1: Shift management pages must load within 2 seconds at P95.
- NFR-2: Bulk shift assignment for up to 500 employees must complete within 5 seconds.
- NFR-3: PostgreSQL RLS must enforce tenant isolation on `shift` and `employee_shift` tables.
- NFR-4: Shift definitions must be cached in Redis with a TTL of 1 hour; cache invalidated on update.

## 6. Business Rules
- BR-1: Every tenant must have at least one default shift. It is created during tenant provisioning.
- BR-2: An employee can have only one active shift at any point in time.
- BR-3: Shift assignments are effective-dated; changes apply from the specified effective_from date.
- BR-4: Grace period defines the number of minutes after shift start_time before a clock-in is considered "late."
- BR-5: Break duration is auto-deducted from total work hours during clock-out calculation (see US-ATT-002).
- BR-6: Working days define which days of the week the shift applies; non-working days are not counted for attendance.
- BR-7: Shifts cannot have start_time equal to end_time (zero-duration shifts are invalid).
- BR-8: For flexible shifts, only minimum_hours is enforced; start/end times are not validated.

## 7. Data Requirements
**shift table:**
| Field | Type | Notes |
|-------|------|-------|
| shift_id | UUID | PK |
| tenant_id | UUID | FK, RLS-enforced |
| name | varchar(100) | Unique per tenant |
| type | varchar(20) | 'SINGLE', 'ROTATING', 'FLEXIBLE' |
| start_time | time | Nullable for FLEXIBLE |
| end_time | time | Nullable for FLEXIBLE |
| break_duration_minutes | integer | Default 0 |
| grace_period_minutes | integer | Default 0 |
| minimum_hours | decimal(4,2) | For FLEXIBLE shifts |
| working_days | integer[] | Array of day numbers (1=Mon, 7=Sun) |
| is_default | boolean | One per tenant |
| is_active | boolean | Soft deactivation |
| created_at / updated_at | timestamptz | Audit |
| created_by / updated_by | UUID | Audit |

**employee_shift table:**
| Field | Type | Notes |
|-------|------|-------|
| employee_shift_id | UUID | PK |
| tenant_id | UUID | FK, RLS-enforced |
| employee_id | UUID | FK |
| shift_id | UUID | FK |
| effective_from | date | Start of assignment |
| effective_to | date | Nullable (null = current) |
| created_at / updated_at | timestamptz | Audit |
| created_by / updated_by | UUID | Audit |

## 8. UI/UX Notes (Notion-like)
- Shift management should be a Notion-style database view with a table listing all shifts and their properties.
- Allow inline editing of shift properties directly in the table (click-to-edit cells).
- Shift assignment should use a multi-select employee picker (searchable dropdown with avatar and employee number).
- Show a calendar-like weekly view for rotating shifts to visualize the rotation pattern.
- Use drag-and-drop to reorder rotation patterns.
- On the employee profile page, show the current shift assignment as a detail card.
- Mobile: use a card layout for shift list; assignment should use a full-screen modal with employee search.
- Provide a "Clone Shift" action button for quick creation of shift variants.

## 9. Dependencies
- Core HR module: Employee records for shift assignment.
- US-ATT-001 / US-ATT-002: Shift determines expected clock-in/out times and break calculations.
- US-ATT-008: Late arrival/early departure tracking depends on shift start/end times and grace period.
- Tenant Admin module: Default shift creation during tenant provisioning.

## 10. Assumptions & Constraints
- Night shifts (where end_time < start_time, e.g., 10 PM to 6 AM) are supported; the system interprets end_time as the next calendar day.
- Rotating shifts cycle indefinitely based on the defined pattern and a reference start date.
- Flexible shifts do not enforce clock-in/out times but do enforce minimum total hours.
- Shift changes do not retroactively affect past attendance records.
- Multi-tenant RLS ensures shift definitions from Tenant A are not visible to Tenant B.
- Phase 1 does not include shift auto-scheduling/optimization algorithms.

## 11. Test Hints
- Test CRUD operations for shifts: create, read, update, and verify soft delete prevention when assigned.
- Test bulk assignment: assign a shift to 100 employees at once, verify all records are created.
- Test effective dating: assign Shift A today, Shift B from next Monday, verify Shift A is active now and Shift B activates on Monday.
- Test default shift: verify a new employee without explicit assignment inherits the tenant's default shift.
- Test rotating shift: define a 2-week rotation, verify the correct shift is calculated for any given date.
- Test night shift: create a shift from 22:00 to 06:00, verify clock-in/out calculations span midnight correctly.
- Test tenant isolation: verify Tenant A's shifts are not visible to Tenant B.
- Test duplicate name prevention: create two shifts with the same name, verify uniqueness constraint.
- Test flexible shift: verify that start/end times are not required and only minimum hours is enforced.
