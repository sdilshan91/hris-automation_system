---
id: TC-LV-215
user_story: US-LV-011
module: Leave Management
priority: critical
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-215: HR manually assigns LOP days — leave_request (HR-Assigned) + ledger entry created + employee notified (happy path)

## 1. Test Objective
Verify the manual LOP assignment path (AC-3, FR-3): an HR Officer calls `POST /api/v1/leaves/assign-lop` with `{ employeeId, dates[], reason }` and the system creates a `leave_request` with status "HR-Assigned" (`is_lop = true`, `lop_source = hr_assigned`), writes the corresponding LOP ledger entry, and queues an employee notification (BR-6).

## 2. Related Requirements
- User Story: US-LV-011
- Acceptance Criteria: AC-3
- Functional Requirements: FR-3, FR-4
- Business Rules: BR-6
- Test Hint §11 (manual LOP)

## 3. Preconditions
- Tenant "acme"; LOP type exists; employee "Mark Otieno" exists.
- HR Officer "Asha" authenticated with `Leave.Manage`/`HR.Officer` (assign-LOP permission).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| employeeId | Mark Otieno | target |
| dates[] | 2 working days | e.g. two unpaid days |
| reason | "Unauthorized absence" | required |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Asha calls `POST /api/v1/leaves/assign-lop` with Mark, 2 dates, reason | 200/201; a `leave_request` is created: `leave_type = LOP`, `status = HR-Assigned`, `is_lop = true`, `lop_source = hr_assigned`, `total_days = 2`, tenant acme, reason recorded. |
| 2 | Inspect `leave_ledger` for Mark | An LOP ledger entry is created reflecting the 2 assigned days (per the LOP entry shape); no positive entitlement balance is created (BR-1). |
| 3 | Verify notification (seam) | An employee notification is queued to Mark (best-effort/non-blocking seam — dispatch DEFERRED on the notifications module; the queued/log-only seam is verified, not a silent gap). |
| 4 | Verify audit | The assignment is audit-logged with actor = Asha (NFR-4 — see TC-LV-223). |

## 6. Postconditions
- An HR-Assigned LOP request + ledger entry exist for Mark; the employee-notification seam fired; the action is audited.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
