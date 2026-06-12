---
id: TC-CHR-093
user_story: US-CHR-001
module: Core HR
priority: high
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-093: Input sanitization -- XSS and SQL injection in employee fields

## 1. Test Objective
Verify that the system properly sanitizes all input fields during employee creation to prevent Cross-Site Scripting (XSS) and SQL injection attacks. Malicious input should be sanitized or rejected.

## 2. Related Requirements
- User Story: US-CHR-001
- Non-Functional Requirements: NFR-2 (implicitly, security)

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with HR Officer role is authenticated in the "acme" tenant context.
- Department and job title exist in the tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| XSS payload (first_name) | `<script>alert('xss')</script>` | Stored XSS attempt |
| XSS payload (last_name) | `<img src=x onerror=alert(1)>` | Image-based XSS |
| SQL injection (email) | `'; DROP TABLE employees; --@test.com` | SQL injection attempt |
| SQL injection (first_name) | `' OR 1=1 --` | SQL injection attempt |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Submit with first_name = `<script>alert('xss')</script>` | Either the input is sanitized (HTML tags stripped/escaped) or the request is rejected with a validation error. |
| 2 | If the employee was created, view the employee profile | The name is displayed as escaped text, NOT executed as JavaScript. No alert box appears. |
| 3 | Submit with email = `'; DROP TABLE employees; --@test.com` | The input is either rejected as invalid email format, or safely handled via parameterized queries. No SQL injection occurs. |
| 4 | Verify the database is intact: `SELECT count(*) FROM employees` | The employees table exists and is not affected by injection. |
| 5 | Submit with first_name = `' OR 1=1 --` | Input is safely stored (if allowed) or rejected. Parameterized queries prevent injection. |
| 6 | Query employees and verify no unintended data is returned | Only the expected employee records are returned; the injection payload did not alter query behavior. |

## 6. Postconditions
- No XSS vulnerabilities exist in the employee form or profile display.
- No SQL injection is possible via employee creation fields.
- Database integrity is maintained.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
