---
id: TC-CHR-225
user_story: US-CHR-009
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-225: Future-dated status change -- not applied today, background job applies it on the effective date (BR-4)

## 1. Test Objective
Verify that a status change with a future effective date is stored but NOT applied immediately. The employee's current status remains unchanged until the background job (or on-access check) processes it on the effective date. This validates BR-4.

## 2. Related Requirements
- User Story: US-CHR-009
- Business Rules: BR-4
- Functional Requirements: FR-3, FR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated in the "acme" tenant context.
- Employee "Bob Wilson" (`emp-004-uuid`) exists with status `active`.
- The background job for applying future-dated status changes is configured.
- Today's date is 2026-06-12.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee | Bob Wilson (emp-004-uuid) | Status: active |
| New Status | suspended | Valid transition |
| Reason | Planned suspension for investigation | Required |
| Effective Date | 2026-06-13 (tomorrow) | Future date |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/tenant/employees/emp-004-uuid/status` with body `{ "newStatus": "suspended", "reason": "Planned suspension for investigation", "effectiveDate": "2026-06-13" }`. | Response status is 200 OK (or 202 Accepted). Response indicates the status change is scheduled. |
| 2 | Immediately query the employee's current status via `GET /api/v1/tenant/employees/emp-004-uuid`. | Employee status is still `active`. The future-dated change has not been applied yet. |
| 3 | Verify the employment history / pending status change record. | A record exists with `effective_date` = 2026-06-13, `new_value` = "suspended", `previous_value` = "active". The record may be marked as "pending" or the status change is simply stored with a future date. |
| 4 | Verify the employee's portal access is still enabled. | User account is still active. The employee can still log in today. |
| 5 | Simulate the passage of time to 2026-06-13 and trigger the daily status application background job. | Job executes and processes the pending status change. |
| 6 | Query the employee's status after the job runs. | Employee status is now `suspended`. |
| 7 | Verify side effects have now been applied. | Portal access is disabled. Leave accrual paused (DEFERRED -- leave module). |
| 8 | Verify the employment history entry reflects the correct effective date. | The history entry shows `effective_date` = 2026-06-13 and `changed_at` reflecting when the job processed it. |

## 6. Postconditions
- Employee status is `suspended` (applied on the effective date).
- Employment history entry exists with the future effective date.
- Side effects (portal access disabled) were applied on the effective date, not before.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
