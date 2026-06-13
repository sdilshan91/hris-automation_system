---
id: TC-LV-103
user_story: US-LV-005
module: Leave Management
priority: high
type: performance
status: draft
created: 2026-06-13
---

# TC-LV-103: Approve/Reject API responds within 500ms P95

## 1. Test Objective
Verify that the approve and reject endpoints meet the latency SLA: P95 response time <= 500ms under representative load, including the balance re-check, ledger/history write, audit, and concurrency-token validation. Notification queuing must be asynchronous and must not add to the response time (NFR-1, NFR-2).

## 2. Related Requirements
- User Story: US-LV-005
- Non-Functional Requirements: NFR-1, NFR-2

## 3. Preconditions
- Tenant "acme" is active with a realistic dataset (e.g., 5,000 employees, thousands of historical ledger rows).
- A pool of pending requests from direct reports is available for actioning.
- Manager authenticated with `Leave.Approve.Team`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Dataset | 5,000 employees | Representative tenant |
| Concurrency | 50 virtual users | Load profile |
| SLA | P95 <= 500ms | Approve and reject |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run a load test issuing `POST /api/v1/leaves/{id}/approve` across the request pool | P95 latency <= 500ms; no errors under nominal load. |
| 2 | Run a load test issuing `POST /api/v1/leaves/{id}/reject` with a reason | P95 latency <= 500ms. |
| 3 | Confirm notification queuing is off the response path | Removing/stubbing the notification seam does not materially change response time (NFR-2: async, non-blocking). |
| 4 | Inspect the query plan for the balance re-check and ledger insert | Uses indexed lookups on `leave_ledger` (tenant_id, employee_id, leave_type_id, leave_year); no full scans. |

## 6. Postconditions
- Approve/reject latency within SLA; notification queuing does not block responses.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
