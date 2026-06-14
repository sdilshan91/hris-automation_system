---
id: TC-LV-229
user_story: US-LV-011
module: Leave Management
priority: medium
type: performance
status: draft
created: 2026-06-14
---

# TC-LV-229: assign-lop (write) and lop-summary (read) respond within platform API SLAs

## 1. Test Objective
Verify the synchronous LOP API endpoints meet the platform SLAs: `GET /api/v1/leaves/lop-summary` (read) within P95 <= 400ms and `POST /api/v1/leaves/assign-lop` (write) within P95 <= 800ms, under representative data volumes.

## 2. Related Requirements
- User Story: US-LV-011
- Functional Requirements: FR-3, FR-5
- Platform SLA (read P95 <= 400ms, write P95 <= 800ms)

## 3. Preconditions
- Tenant "acme" with realistic LOP data (e.g. several hundred LOP entries across employees in a period).
- HR Officer "Asha" authenticated with `Leave.Manage`/`HR.Officer`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| lop-summary read | one employee, one month | indexed by tenant/employee/date |
| assign-lop write | 2 dates | single employee |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Issue repeated `GET /lop-summary?employeeId&from&to` and measure P95 | P95 <= 400ms; the query uses tenant/employee/date indexing on `leave_request` (is_lop filtered). |
| 2 | Issue repeated `POST /assign-lop` (2 dates) and measure P95 | P95 <= 800ms including ledger write + audit. |
| 3 | Vary period width (1 month vs 12 months) | Read latency scales sub-linearly and stays within SLA for typical ranges. |
| 4 | Record results | Latency documented against the SLA targets. |

## 6. Postconditions
- The LOP read/write endpoints meet the platform read/write SLAs.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
