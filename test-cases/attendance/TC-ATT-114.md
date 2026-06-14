---
id: TC-ATT-114
user_story: US-ATT-008
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-114: Late policy configuration -- HR reads and updates the tenant late_policy (threshold, deduction days, period, notification + chronic flags) via GET/PUT (FR-4)

## 1. Test Objective
Verify FR-4: HR can read and configure the tenant-level late_policy through `GET /api/v1/attendance/late-policy` and `PUT /api/v1/attendance/late-policy`, covering threshold_count, deduction_days, period, notification_on_late, chronic_threshold, and is_active. The saved policy then drives detection/deduction/notification behaviour (TC-ATT-107/108/109) and validation rejects invalid values.

## 2. Related Requirements
- User Story: US-ATT-008
- Functional Requirements: FR-4 (tenant-configurable late arrival policies: warning threshold, deduction rules, notification triggers, chronic threshold)
- Data: late_policy (S7)
- API: GET /api/v1/attendance/late-policy, PUT /api/v1/attendance/late-policy

## 3. Preconditions
- Tenant "acme"; HR Officer "Priya" authenticated with the attendance-policy management permission (HR-only).
- An existing or default late_policy row for acme.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| threshold_count | 3 | lates -> deduction |
| deduction_days | 0.5 | decimal(3,1) |
| period | MONTHLY | enum MONTHLY/QUARTERLY |
| notification_on_late | true | gates per-late notification |
| chronic_threshold | 5 | HR escalation |
| is_active | true | |
| invalid threshold_count | -1 / 0 | must be rejected |
| invalid period | WEEKLY | not in enum -> rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Priya, `GET /api/v1/attendance/late-policy` | 200; returns acme's current late_policy (tenant-scoped). |
| 2 | As Priya, `PUT /api/v1/attendance/late-policy` with the valid Test Data | 200; the policy is persisted with tenant_id = acme stamped server-side (not client-supplied). |
| 3 | Re-`GET` the policy | The updated values are returned and now govern detection/deduction (cross-check via TC-ATT-107) and notifications (TC-ATT-109). |
| 4 | `PUT` with threshold_count = -1 (and again 0) | 400 with a validation error; the policy is unchanged. |
| 5 | `PUT` with period = WEEKLY (out of enum) | 400 validation error; unchanged. |
| 6 | `PUT` with deduction_days exceeding decimal(3,1) precision (e.g. 2 decimals) | 400 or rounded per the contract; record the behaviour. |
| 7 | Set is_active = false and re-run a late clock-in | With the policy inactive, deduction/notification gating from TC-ATT-107/109 does not apply; is_late on the log is still computed (detection is independent of the deduction policy). |

## 6. Postconditions
- The acme late_policy reflects the last valid update, tenant-scoped; invalid updates were rejected without mutating the policy.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- Detection (is_late/late_minutes per FR-1/FR-3) is independent of the late_policy being active -- the policy governs DEDUCTION and NOTIFICATION, not whether lateness is computed (Step 7). **Reported to caller** if detection is gated on an active policy.
- Authn/authz (HR-only, tenant-scoped) for this endpoint is covered in TC-ATT-117; tenant isolation of the policy row in TC-ATT-ISO-011.
