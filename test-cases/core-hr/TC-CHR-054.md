---
id: TC-CHR-054
user_story: US-CHR-005
module: Core HR
priority: high
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-054: Input sanitization -- XSS and SQL injection in job title name

## 1. Test Objective
Verify that the system properly sanitizes input for the `title_name` and `description` fields, preventing XSS payloads from being stored/rendered and SQL injection attempts from affecting the database.

## 2. Related Requirements
- User Story: US-CHR-005
- Functional Requirements: FR-1
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| XSS payload 1 | `<script>alert('xss')</script>` | Script injection |
| XSS payload 2 | `"><img src=x onerror=alert(1)>` | Attribute injection |
| SQL injection 1 | `'; DROP TABLE job_titles; --` | SQL injection attempt |
| SQL injection 2 | `' OR 1=1 --` | SQL injection attempt |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `POST /api/v1/job-titles` with `title_name: "<script>alert('xss')</script>"` | Response is either 400 Bad Request (input rejected) or 201 Created with the value stored as escaped/sanitized text. |
| 2 | If the XSS payload was stored (step 1 returned 201), retrieve the job title via `GET /api/v1/job-titles/{id}` | The `title_name` is returned as escaped text, NOT as executable HTML/JavaScript. |
| 3 | If stored, navigate to the Job Titles management page and verify the XSS payload row | The browser does NOT execute the script. The text is rendered as literal characters. |
| 4 | Call `POST /api/v1/job-titles` with `title_name: "'; DROP TABLE job_titles; --"` | Response is 201 Created (parameterized queries prevent injection) or 400 Bad Request. The `job_titles` table is NOT affected. |
| 5 | Verify the `job_titles` table still exists and contains all expected records | No data loss or corruption occurred. |
| 6 | Call `POST /api/v1/job-titles` with `description: ""><img src=x onerror=alert(1)>"` | Response handles the payload safely (escaped or rejected). |
| 7 | Verify no JavaScript execution occurs when rendering the description in the UI | Payload is displayed as literal text. |

## 6. Postconditions
- No XSS payloads execute in the browser.
- No SQL injection affects the database.
- The system is protected by parameterized queries (EF Core) and output encoding.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
