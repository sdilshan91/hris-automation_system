---
id: TC-ATT-113
user_story: US-ATT-008
module: Attendance
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-ATT-113: Employee lateness score -- my-score returns "X of N allowed lates used this month" for the self-service dashboard (FR-5/§8)

## 1. Test Objective
Verify the employee self-service lateness score (§8 + AC-4 context): `GET /api/v1/attendance/late-early/my-score?month=` returns the employee's own month-to-date late count and the allowed-lates threshold so the dashboard can render "X of N allowed lates used this month" (e.g. "2 of 3"). Self-scoped -- an employee sees only their own score.

## 2. Related Requirements
- User Story: US-ATT-008
- UI/UX Notes: §8 (monthly "lateness score"/progress indicator -- "2 of 3 allowed lates used this month")
- Functional Requirements: FR-5 (monthly late count surfaced to the employee), FR-4 (threshold from late_policy)
- API: GET /api/v1/attendance/late-early/my-score?month=

## 3. Preconditions
- Tenant "acme"; late_policy with threshold_count = 3 (allowed lates before deduction), period MONTHLY.
- Employee "Asha" authenticated with self-scope; 2 late arrivals recorded this month.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| month | current | query param |
| threshold_count (N) | 3 | allowed lates |
| late count (X) | 2 | month-to-date |
| early_departure_count | (surfaced) | optional companion metric |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Asha, `GET /late-early/my-score?month=<current>` | 200; payload includes late_count = 2, allowed = 3 (from late_policy threshold), enabling the "2 of 3 allowed lates used this month" indicator (§8). Tenant_id = acme, employee = Asha. |
| 2 | Record a 3rd late, re-query | late_count = 3 -> indicator shows "3 of 3" (at threshold); aligns with the deduction flag in TC-ATT-107. |
| 3 | Query a prior month | The score recomputes for the requested month only (month-scoped). |
| 4 | Verify self-scope | Asha cannot pass another employee's id to read their score; the endpoint resolves the employee from the authenticated identity (cross-employee/cross-tenant blocked -- see TC-ATT-117/ISO-011). |
| 5 | No-policy fallback | If no active late_policy exists, the allowed/threshold is absent or null and the indicator degrades to a plain late count (record the contract). |

## 6. Postconditions
- No state change; Asha's own month lateness score is returned for the dashboard, tenant- and employee-scoped.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Notes
- The "allowed" N is the late_policy.threshold_count (the lates allowed before a deduction). **Reported to caller** if the dashboard expects a separate "allowed lates" config distinct from the deduction threshold -- S7 exposes only threshold_count, so this TC maps N to that. The self-scope enforcement detail is shared with TC-ATT-117.
