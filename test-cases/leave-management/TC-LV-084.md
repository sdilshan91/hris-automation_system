---
id: TC-LV-084
user_story: US-LV-004
module: Leave Management
priority: high
type: security
status: draft
created: 2026-06-13
---

# TC-LV-084: Input sanitization -- malicious filter/query parameters do not cause injection or XSS

## 1. Test Objective
Verify that the pending queue's filter and query parameters (leave type, employee, date range, sort) are validated/parameterized so that SQL-injection and XSS payloads are neutralized: no injection executes, and any reflected values are encoded in the UI.

## 2. Related Requirements
- User Story: US-LV-004
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-3

## 3. Preconditions
- Tenant "acme" is active.
- Manager "Robert Lee" is authenticated with `Leave.Approve.Team`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| SQLi in employeeId | `' OR '1'='1` | Injection attempt |
| SQLi in sortBy | `requestedAt; DROP TABLE leave_request;--` | Injection attempt |
| XSS in free filter | `<script>alert('xss')</script>` | Reflected XSS attempt |
| Malformed date | `2026-13-40` | Invalid date |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/leaves/pending?employeeId=' OR '1'='1` | Treated as an invalid/unmatched id -> 400 or empty result; no rows from outside scope returned; no SQL executed. |
| 2 | Call the endpoint with `sortBy=requestedAt; DROP TABLE leave_request;--` | Rejected/ignored as an invalid sort field (whitelist); the `leave_request` table is intact afterwards. |
| 3 | Submit an XSS payload in any reflected text filter and view the active-filter chip | The payload is rendered as inert text (HTML-encoded); no script executes in the browser. |
| 4 | Submit a malformed date `2026-13-40` in the range filter | 400 validation error; no unhandled exception. |
| 5 | Verify parameterized queries | Filters are bound as parameters via EF Core, not string-concatenated SQL. |

## 6. Postconditions
- No data mutated; `leave_request` table intact.
- Filter inputs are validated/encoded; no injection or XSS.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
