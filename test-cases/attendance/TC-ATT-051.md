---
id: TC-ATT-051
user_story: US-ATT-005
module: Attendance
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-051: Create a SINGLE (fixed) shift -- saved with tenant_id and available for assignment (happy path)

## 1. Test Objective
Verify the primary shift-definition flow (AC-1, FR-1, FR-2): an HR Officer creates a new SINGLE shift with name, start_time, end_time, break_duration, grace_period, and working_days; the system persists a `shift` row stamped with the session tenant_id, marks it active, and immediately exposes it for assignment (it appears in the shift list and in the assignment employee-picker's shift selector).

## 2. Related Requirements
- User Story: US-ATT-005
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1 (SINGLE type), FR-2 (shift parameters)
- Data: `shift` table (tenant_id RLS-enforced, name unique per tenant)

## 3. Preconditions
- Tenant "acme" `active`, Attendance module enabled.
- HR Officer "Priya Shah" authenticated, holds `Attendance.Shift.Manage`.
- No existing shift named "Day Shift" in acme.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| name | "Day Shift" | Unique per tenant |
| type | SINGLE | Fixed shift |
| start_time | 09:00 | tenant-local time |
| end_time | 17:00 | tenant-local time |
| break_duration_minutes | 60 | |
| grace_period_minutes | 10 | Late threshold (see US-ATT-008) |
| working_days | [1,2,3,4,5] | Mon-Fri |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Priya, `POST /api/v1/attendance/shifts` with the test-data body | Response 201; payload returns the new shift_id, type SINGLE, the supplied parameters, is_active true. |
| 2 | Inspect the persisted `shift` row | Row exists with tenant_id = acme (stamped by TenantInterceptor, NOT from any request body field), name "Day Shift", start/end 09:00/17:00, break 60, grace 10, working_days [1..5], created_by = Priya, created_at set. |
| 3 | `GET /api/v1/attendance/shifts` as Priya | The new shift is listed for acme. |
| 4 | Open the shift-assignment employee picker / shift selector | "Day Shift" is selectable as an assignment target (it is available for assignment). |
| 5 | Verify audit | An audit_log entry records the shift create with actor = Priya, tenant-scoped. |

## 6. Postconditions
- A tenant-scoped, active SINGLE shift "Day Shift" exists and is available for employee assignment.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
