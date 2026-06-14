---
id: TC-LV-163
user_story: US-LV-008
module: Leave Management
priority: high
type: security
status: draft
created: 2026-06-14
---

# TC-LV-163: Input sanitization -- malicious `year` parameter on the preview API is rejected/neutralized (NFR-2)

## 1. Test Objective
Verify the carry-forward preview endpoint safely handles malicious or malformed `year` query parameters: SQL-injection and XSS payloads are parameterized/validated and never executed, and out-of-range/non-numeric years are rejected with a clean 400 rather than leaking an error or data (NFR-2).

## 2. Related Requirements
- User Story: US-LV-008
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-5

## 3. Preconditions
- Tenant "acme"; HR Officer "Priya" authenticated with leave-config permission.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| SQLi payload | `year=2026; DROP TABLE leave_ledger;--` | must not execute |
| XSS payload | `year=<script>alert(1)</script>` | must not reflect/execute |
| Out-of-range | `year=99999` / `year=-1` | rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call the preview endpoint with the SQLi payload as `year` | 400 Bad Request (invalid integer); `leave_ledger` is intact; no SQL executed (EF parameterization). |
| 2 | Call with the XSS payload as `year` | 400 Bad Request; payload is neither executed nor reflected unescaped in the response. |
| 3 | Call with out-of-range/non-numeric years | 400 with a generic validation message; no stack trace or internal detail leaked. |
| 4 | Call with a valid year | 200 OK with the expected projection -- confirming the guard does not over-reject valid input. |

## 6. Postconditions
- Malicious/malformed `year` input is rejected/neutralized; no injection, reflection, or data leakage.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
