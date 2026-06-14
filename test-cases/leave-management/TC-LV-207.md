---
id: TC-LV-207
user_story: US-LV-010
module: Leave Management
priority: high
type: security
status: draft
created: 2026-06-14
---

# TC-LV-207: Cancellation reason is sanitized -- XSS / injection payloads stored and rendered safely (security; NFR-2)

## 1. Test Objective
Verify that a cancellation `reason` containing an XSS payload or SQL-injection-like text is stored as inert data and rendered escaped wherever it is later displayed (approval history, manager notification, audit log), with no script execution and no SQL injection (parameterized queries) (NFR-2).

## 2. Related Requirements
- User Story: US-LV-010
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-2, FR-5, FR-6

## 3. Preconditions
- Tenant "acme".
- Employee "Jane Smith" has an APPROVED future request R (reason mandatory).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| XSS reason | `<script>alert('x')</script>` | must not execute |
| Quote/SQL reason | `'; DROP TABLE leave_request;--` | must be inert via parameterization |
| Long reason | 5000 chars | length-bound check |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Cancel R with the XSS reason | Stored as a literal string; when the reason is rendered (history/notification/audit views), it is HTML-escaped and does not execute. |
| 2 | Cancel another approved request with the SQL-injection reason | Persisted as a literal string via parameterized query; the `leave_request` table is intact; no injection occurs. |
| 3 | Submit an over-length reason (5000 chars) | Either accepted within the documented text limit or rejected with a clear length-validation error -- never truncated silently in a way that corrupts the audit record. |
| 4 | Inspect the audit log and approval history | The reason is stored verbatim (escaped on render); no markup is interpreted server- or client-side. |

## 6. Postconditions
- Reason input is treated as inert data end-to-end; no XSS execution, no SQL injection, length bound enforced.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [x] Boundary test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
