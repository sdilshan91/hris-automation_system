---
id: TC-LV-102
user_story: US-LV-005
module: Leave Management
priority: high
type: security
status: draft
created: 2026-06-13
---

# TC-LV-102: Input sanitization -- XSS/SQL-injection payloads in the approval comment and rejection reason

## 1. Test Objective
Verify that the optional approval comment and the mandatory rejection reason are safely handled: malicious payloads (XSS script, SQL-injection strings) are stored as inert text (parameterized queries) and rendered escaped in the UI/history, with no script execution and no SQL side effects.

## 2. Related Requirements
- User Story: US-LV-005
- Non-Functional Requirements: NFR-3
- Functional Requirements: FR-1, FR-2

## 3. Preconditions
- Tenant "acme" is active; Manager "Robert Lee" authenticated with `Leave.Approve.Team`.
- Pending requests from direct reports exist.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| XSS comment | `<script>alert('x')</script>` | Approval comment |
| XSS reason | `<img src=x onerror=alert(1)>` | Rejection reason |
| SQLi reason | `'; DROP TABLE leave_approval_history;--` | Rejection reason |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Approve a request with the XSS comment payload | 200; the comment is stored verbatim as text in `leave_approval_history` and rendered HTML-escaped; no script executes. |
| 2 | Reject a request with the XSS reason payload | 200; reason stored as inert text, rendered escaped in history/notification; no script executes. |
| 3 | Reject another request with the SQLi reason payload | 200; the string is stored literally via parameterized query; `leave_approval_history` table still exists and other rows are intact (no injection). |
| 4 | View the affected requests' history in the UI | Payloads display as literal text, not active markup; the page is not compromised. |

## 6. Postconditions
- Payloads stored inertly; no XSS execution, no SQL injection side effects.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
