---
id: TC-ATT-076
user_story: US-ATT-006
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-076: Manager rejects overtime with a mandatory reason; rejection without a reason is refused (negative)

## 1. Test Objective
Verify FR-6 (reject): a manager can reject a PENDING overtime record with a reason (`POST /api/v1/attendance/overtime/{id}/reject` { reason }), setting status REJECTED, excluding it from payroll, and recording the reason; a rejection submitted WITHOUT a reason (or below the minimum length) is refused and the record stays PENDING.

## 2. Related Requirements
- User Story: US-ATT-006
- Acceptance Criteria: AC-4 (reject variant)
- Functional Requirements: FR-6 (approve, reject, or adjust)

## 3. Preconditions
- Tenant "acme". Manager "Ben"; direct report "Asha" has a PENDING overtime_record.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| valid reject body | { reason: "Overtime was not authorised in advance." } | >= min length |
| empty reject body | { } / { reason: "" } | refused |
| short reason | "no" | below min length -> refused |
| expected status (valid) | REJECTED | |
| expected payroll-ready (rejected) | false / excluded | |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Ben, reject with no `reason` | 400/validation error; record stays PENDING; not payroll-ready. |
| 2 | As Ben, reject with a too-short reason | 400/validation error; record stays PENDING (mandatory, min-length reason). |
| 3 | As Ben, reject with a valid reason | 200; status -> REJECTED, reason stored, NOT payroll-ready; tenant-scoped. |
| 4 | `GET /overtime/pending` as Ben | The rejected record no longer appears in PENDING. |
| 5 | `GET /overtime/my` as Asha | Asha's record shows REJECTED with the rejection reason visible (red tag per §8). |
| 6 | Audit | The rejection is audited: actor=Ben, timestamp, target overtime_id, reason. |

## 6. Postconditions
- A rejected overtime record carries a mandatory reason and is excluded from payroll; rejection without a reason is blocked and leaves the record PENDING.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- The exact minimum reason length should match the backend validator; this TC asserts the mandatory-reason rule (consistent with the regularization rejection rule, US-ATT-004 TC-ATT-039, reason >= 10 chars) and pins empty/too-short as refused.
- Employee notification of the rejection (with reason) is the dispatch seam DEFERRED on US-NTF, consistent with TC-ATT-038. **Reported to caller.**
