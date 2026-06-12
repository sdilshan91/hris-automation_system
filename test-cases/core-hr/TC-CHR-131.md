---
id: TC-CHR-131
user_story: US-CHR-003
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-131: Invalid and out-of-range page parameter handled gracefully

## 1. Test Objective
Verify that the directory API and UI handle invalid, out-of-range, and edge-case pagination parameters gracefully without errors or data leakage. This is a negative test for AC-4 and FR-5.

## 2. Related Requirements
- User Story: US-CHR-003
- Acceptance Criteria: AC-4
- Functional Requirements: FR-5

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- 25 employees exist (2 pages at page size 20, 3 pages if page size is reduced).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Valid range | page=1 to page=2 | 25 employees / 20 per page |
| Out-of-range page | page=999 | Beyond total pages |
| Negative page | page=-1 | Invalid |
| Zero page | page=0 | Invalid |
| Non-numeric page | page=abc | Invalid |
| Invalid pageSize | pageSize=-5 | Invalid |
| pageSize=0 | pageSize=0 | Edge case |
| Extreme pageSize | pageSize=10000 | Exceeds reasonable limits |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/tenant/employees/directory?page=999&pageSize=20` | Response is 200 OK with empty `data` array, `total: 25`, `page: 999`, `pageSize: 20` (or server clamps to last valid page with empty results). No 500 error. |
| 2 | Navigate to the directory URL with `?page=999` in the browser | UI shows empty state or redirects to the last valid page. No crash or uncaught exception. |
| 3 | Send `GET /api/v1/tenant/employees/directory?page=-1&pageSize=20` | Response is 400 Bad Request with validation error, or server clamps to page=1. |
| 4 | Send `GET /api/v1/tenant/employees/directory?page=0&pageSize=20` | Response is 400 Bad Request with validation error, or server clamps to page=1. |
| 5 | Send `GET /api/v1/tenant/employees/directory?page=abc&pageSize=20` | Response is 400 Bad Request with validation error for non-numeric value. |
| 6 | Send `GET /api/v1/tenant/employees/directory?page=1&pageSize=-5` | Response is 400 Bad Request with validation error. |
| 7 | Send `GET /api/v1/tenant/employees/directory?page=1&pageSize=0` | Response is 400 Bad Request with validation error, or server uses default page size. |
| 8 | Send `GET /api/v1/tenant/employees/directory?page=1&pageSize=10000` | Server either clamps to max allowed pageSize (e.g., 50) or returns all results; no timeout or memory error. |

## 6. Postconditions
- No data was modified.
- No 500 Internal Server Error was returned for any input.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
