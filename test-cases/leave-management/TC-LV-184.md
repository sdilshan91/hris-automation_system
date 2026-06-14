---
id: TC-LV-184
user_story: US-LV-009
module: Leave Management
priority: high
type: security
status: draft
created: 2026-06-14
---

# TC-LV-184: Input sanitization on team-calendar query parameters (from/to/employeeId/leaveType/status) (NFR-3)

## 1. Test Objective
Verify the team-calendar endpoint safely handles malicious or malformed query parameters (SQL injection, XSS payloads, non-GUID ids, non-date values) without leaking data, erroring with a 500, or executing injected SQL.

## 2. Related Requirements
- User Story: US-LV-009
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-1, FR-6

## 3. Preconditions
- Tenant "acme"; Manager "Maya" authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| employeeId | `' OR '1'='1` | SQLi probe |
| leaveType | `<script>alert(1)</script>` | XSS probe |
| from | `not-a-date` | malformed date |
| status | `Approved'; DROP TABLE leave_request;--` | SQLi probe |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send the SQLi `employeeId` payload | Parameterized query / GUID parsing rejects it with 400 (or returns empty); no injection, no all-tenant leak. |
| 2 | Send the XSS `leaveType` payload | The value is treated as data (no match) and any echoed text is encoded; no script executes in the rendered calendar. |
| 3 | Send a malformed `from` date | 400 Bad Request with a validation message; no 500, no unbounded scan. |
| 4 | Send the SQLi `status` payload | Rejected as an invalid enum value (400); the leave_request table is intact. |

## 6. Postconditions
- All injection/XSS/malformed inputs are neutralized; no data leak, no server error, DB intact.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
