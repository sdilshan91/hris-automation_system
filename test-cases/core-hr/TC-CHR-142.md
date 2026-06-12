---
id: TC-CHR-142
user_story: US-CHR-003
module: Core HR
priority: high
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-142: XSS and SQL injection in search and filter parameters

## 1. Test Objective
Verify that the directory search and filter endpoints properly sanitize inputs to prevent Cross-Site Scripting (XSS) and SQL Injection attacks. Malicious payloads must be treated as literal strings and not executed.

## 2. Related Requirements
- User Story: US-CHR-003
- Functional Requirements: FR-1, FR-2
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- Employees exist in the "acme" tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| XSS payload (search) | `<script>alert('xss')</script>` | Must not execute |
| XSS payload (filter) | `<img onerror=alert(1) src=x>` | Must not execute |
| SQL injection (search) | `' OR 1=1 --` | Must not bypass query |
| SQL injection (search) | `'; DROP TABLE employees; --` | Must not execute |
| SQL injection (filter department) | `Engineering' OR '1'='1` | Must not bypass filter |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `GET /api/v1/tenant/employees/directory?search=<script>alert('xss')</script>` | Response is 200 OK with empty results (no match). No script execution in the response. |
| 2 | Render the search results in the browser with the XSS payload in the URL | No JavaScript alert dialog pops up. The payload is displayed as escaped text or returns empty state. |
| 3 | Send `GET /api/v1/tenant/employees/directory?search=' OR 1=1 --` | Response is 200 OK with empty results (literal string search, not SQL injection). Does NOT return all employees. |
| 4 | Send `GET /api/v1/tenant/employees/directory?search='; DROP TABLE employees; --` | Response is 200 OK with empty results. The employees table is NOT dropped. |
| 5 | Verify employees still exist | `GET /api/v1/tenant/employees/directory` returns all employees normally. |
| 6 | Send `GET /api/v1/tenant/employees/directory?departments=Engineering' OR '1'='1` | Response is 200 OK with zero results (no department matches the literal injected string). |
| 7 | Send filter with `<img onerror=alert(1) src=x>` as department | Response returns empty results. No image tag rendered. |
| 8 | Verify no 500 Internal Server Error for any payload | All responses are either 200 (with empty/correct data) or 400 (validation error). |

## 6. Postconditions
- No data was modified or destroyed.
- No XSS or SQL injection was successful.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
