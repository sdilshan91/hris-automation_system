---
id: TC-LV-226
user_story: US-LV-011
module: Leave Management
priority: high
type: security
status: draft
created: 2026-06-14
---

# TC-LV-226: Input sanitization — LOP reason and compulsory-leave reason are stored/rendered safely (XSS / SQL injection)

## 1. Test Objective
Verify that free-text fields on the LOP surface (the `reason` on assign-lop, the compulsory-leave `reason`, and any override reason) are safe against XSS and SQL injection: payloads are persisted as inert data (parameterized, no execution) and rendered escaped in the LOP management UI and lop-summary responses.

## 2. Related Requirements
- User Story: US-LV-011
- Functional Requirements: FR-3, FR-6
- Non-Functional Requirements: NFR-2
- Data §7 (compulsory_leave.reason)

## 3. Preconditions
- Tenant "acme"; LOP type exists; employee "Mark Otieno".
- HR Officer "Asha" authenticated with `Leave.Manage`/`HR.Officer`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| XSS payload | `<script>alert('lop')</script>` | reason field |
| SQLi payload | `'; DROP TABLE leave_request;--` | reason field |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | assign-lop for Mark with the XSS payload as `reason` | The value is stored verbatim as text (EF parameterized); no script executes. |
| 2 | Render the LOP entry in the HR LOP management list / lop-summary | The payload renders escaped as literal text; no DOM execution (Angular escapes by default). |
| 3 | assign-lop with the SQLi payload as `reason` | Stored as literal text; the `leave_request`/`leave_ledger` tables are intact (no injection); query still parameterized. |
| 4 | Verify persistence integrity | Subsequent lop-summary queries return correct data; no table dropped/corrupted. |

## 6. Postconditions
- LOP free-text fields are injection-safe at storage and render; no XSS/SQLi side effects.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
