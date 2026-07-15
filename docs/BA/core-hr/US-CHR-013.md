---
id: US-CHR-013
module: Core HR
priority: Should Have
persona: HR Manager / HR Officer
status: draft
sprint: backlog
created: 2026-07-15
acceptance_criteria_count: 2
---

# US-CHR-013: Employee FTE & Work Arrangement

## 1. Description
**As an** HR Manager or HR Officer,
**I want to** record each employee's full-time-equivalent (FTE) ratio and work arrangement (on-site / hybrid / remote),
**So that** the system can model part-time and remote/hybrid staff correctly — prorating leave entitlement by FTE and exempting remote workers from geofenced clock-in.

## 2. Preconditions
- The user is authenticated with HR Manager, HR Officer, or Tenant Admin role within their tenant.
- The employee record exists (US-CHR-001) or is being created.
- The Core HR module is enabled for the tenant.

## 3. Acceptance Criteria (IEEE 830 §3.2 - Specific Requirements)
| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | An HR Officer creates or edits an employee | They set the employee's `Fte` value | The value is accepted only when `0 < Fte <= 1.0` (default 1.0, 2 decimal places); values of `0`, negatives, or `> 1.0` are rejected. The stored FTE prorates the employee's leave entitlement (feeds US-LV-002 pro-rata) |
| AC-2 | An HR Officer creates or edits an employee | They set the employee's `WorkArrangement` (OnSite / Hybrid / Remote, default OnSite) | The value is accepted only if it is a defined enum member; unknown values are rejected. A `Remote` employee is exempt from geofence enforcement at clock-in (feeds US-ATT-001 / US-ATT-011), while OnSite/Hybrid employees remain subject to geofence policy |

## 4. Functional Requirements (IEEE 830 §3.2)
- FR-1: The system SHALL add `Employee.Fte` (decimal, default `1.0`, 2 dp) settable on employee create and edit.
- FR-2: The system SHALL validate `Fte` to the range `0 < Fte <= 1.0` and reject out-of-range or non-2dp values with a clear message.
- FR-3: The system SHALL wire `Employee.Fte` into leave entitlement pro-rata calculation (replacing the previously hardcoded `1.0` at every call site), fulfilling US-LV-002 BR-2 / AC-K1.
- FR-4: The system SHALL add `Employee.WorkArrangement` (enum: OnSite=0, Hybrid=1, Remote=2, default OnSite) settable on create and edit, validated to defined enum members.
- FR-5: The system SHALL exempt `Remote` employees from geofence enforcement at clock-in; OnSite/Hybrid remain subject to the tenant/location geofence policy.

## 5. Non-Functional Requirements (IEEE 830 §3.3)
- NFR-1: FTE and work-arrangement fields SHALL be tenant-isolated via PostgreSQL RLS and EF Core global query filters (they live on the already-isolated `employee` row).
- NFR-2: FTE and work-arrangement changes SHALL be captured in the employee audit trail (before/after).
- NFR-3: The employee create/edit form SHALL meet WCAG 2.1 AA (labelled inputs, keyboard navigable).

## 6. Business Rules
- BR-1: `Fte` must satisfy `0 < Fte <= 1.0`; a full-time employee is `1.0` (the default). Invalid values are rejected server-side, not silently clamped.
- BR-2: FTE prorates leave entitlement proportionally (a 0.5-FTE employee receives half the full-year entitlement), consistent with US-LV-002 BR-2.
- BR-3: `WorkArrangement` accepts only defined enum values; the default is OnSite.
- BR-4: Only `Remote` grants geofence exemption; Hybrid employees are still geofence-enforced (they attend on-site part of the time).

## 7. Data Requirements
**employee (added columns):**
| Field | Type | Notes |
|-------|------|-------|
| fte | numeric(3,2) | Default 1.00; range `0 < fte <= 1.00` |
| work_arrangement | integer (enum) | OnSite=0 (default), Hybrid=1, Remote=2 |

**Output:** employee object echoes `fte` and `workArrangement`; both appear on the profile (US-CHR-002).

## 8. UI/UX Notes
- Add an "FTE" numeric input (step 0.01, min just above 0, max 1.00) in the Employment Details section of the employee wizard/profile, defaulting to 1.00 with a helper note ("1.00 = full-time; 0.50 = half-time").
- Add a "Work Arrangement" select (On-site / Hybrid / Remote) defaulting to On-site, with a helper note that Remote exempts the employee from location-based clock-in.
- Both fields are visible on the employee profile summary (read-only for the Employee self-service persona).

## 9. Dependencies
- US-CHR-001 / US-CHR-002 — own the employee create/edit and profile surfaces; FTE + WorkArrangement fields are settable there.
- US-LV-002 — consumes `Employee.Fte` for entitlement pro-rata (previously-deferred AC-K1).
- US-ATT-001 / US-ATT-011 — consume `WorkArrangement == Remote` for geofence exemption; location-scoped geofence policy is owned by US-ATT-011.

## 10. Assumptions & Constraints
- FTE affects entitlement pro-rata; it does not, by itself, change salary or working-hours definitions in Phase 1 (OT-base FTE scaling is a separate opt-in flag on US-ATT-011).
- Migrations are CLI-generated only; the new columns live on the existing tenant-isolated `employee` row (its RLS policy already applies).
- Bulk import still writes only the legacy free-text location and does not yet carry FTE/work-arrangement (parked; out of scope here).

## 11. Test Hints
- Set `Fte = 0.5` on a mid-nothing full-year employee and verify leave entitlement is exactly half.
- Validation: reject `Fte = 0`, `Fte = -0.1`, `Fte = 1.5`, and `Fte = 0.333` (precision); accept `1.00` and `0.50`.
- Set `WorkArrangement = Remote` and verify a clock-in outside the geofence succeeds; set `OnSite` and verify it is blocked.
- Validation: reject an undefined `WorkArrangement` value.
- Tenant isolation: verify FTE/work-arrangement edits in Tenant A never touch Tenant B (RLS + query filter).
