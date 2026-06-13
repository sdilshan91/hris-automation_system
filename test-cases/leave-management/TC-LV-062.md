---
id: TC-LV-062
user_story: US-LV-003
module: Leave Management
priority: high
type: security
status: draft
created: 2026-06-13
---

# TC-LV-062: Input sanitization -- XSS payload in the leave reason field

## 1. Test Objective
Verify that script/HTML payloads entered in the leave `reason` field (and other free-text inputs) are safely stored and rendered, with no script execution when the request is later viewed by a manager or employee.

## 2. Related Requirements
- User Story: US-LV-003
- Non-Functional Requirements: NFR-4 (tenant isolation pipeline), input safety

## 3. Preconditions
- Tenant "acme" is active; Employee "Jane Smith" is authenticated with `Leave.Apply`.
- A valid active leave type and balance exist.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Reason payload 1 | `<script>alert('xss')</script>` | Script injection |
| Reason payload 2 | `<img src=x onerror=alert(1)>` | Event-handler injection |
| Reason payload 3 | `"><svg/onload=alert(1)>` | Attribute-break injection |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Submit a leave request with Reason = `<script>alert('xss')</script>` | Request is accepted; the payload is stored as literal text (not executed). |
| 2 | View the request in the employee's "My Leaves" list | The reason renders as escaped text; no alert dialog fires; no DOM injection occurs. |
| 3 | View the request in the manager's approval queue | Same: reason is escaped/encoded on output; no script executes. |
| 4 | Repeat with payloads 2 and 3 | All payloads are stored and rendered as inert text. |
| 5 | Inspect the stored DB value | Value is stored verbatim (parameterized query -- no SQL injection); output encoding happens at render time. |

## 6. Postconditions
- No stored or reflected XSS executes in any view of the leave request.
- Free-text fields are output-encoded; queries are parameterized (no SQL injection).

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
