---
id: TC-LV-124
user_story: US-LV-006
module: Leave Management
priority: high
type: security
status: draft
created: 2026-06-14
---

# TC-LV-124: Input sanitization -- malicious year/leaveTypeId query params (SQLi/XSS) are rejected or neutralized

## 1. Test Objective
Verify that the my-ledger / my-balance query parameters (`year`, `leaveTypeId`) are validated and parameterized so that SQL-injection and XSS payloads neither execute, error the server, nor leak cross-employee/cross-tenant data (NFR-3).

## 2. Related Requirements
- User Story: US-LV-006
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-3

## 3. Preconditions
- Tenant "acme" active; employee "Nina Patel" authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| leaveTypeId | `' OR '1'='1` | SQLi probe |
| leaveTypeId | `<script>alert(1)</script>` | XSS probe |
| year | `2026; DROP TABLE leave_ledger;--` | SQLi probe |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `my-ledger?leaveTypeId=' OR '1'='1&year=2026` | 400 Bad Request (invalid GUID) or empty/safe result; no SQL executed; query is parameterized. |
| 2 | Call `my-ledger?year=2026; DROP TABLE leave_ledger;--` | 400 Bad Request (invalid year); `leave_ledger` table remains intact. |
| 3 | Submit an XSS payload as `leaveTypeId` and render any error/echo in the UI | Payload is encoded/escaped; no script executes in the browser. |
| 4 | Verify isolation under injection | No injection returns another employee's or tenant's rows (query still bound by identity + tenant filter). |

## 6. Postconditions
- Parameters are validated/parameterized; no injection, no data leak, no schema damage.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
