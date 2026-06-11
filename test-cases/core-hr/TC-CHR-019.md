---
id: TC-CHR-019
user_story: US-CHR-004
module: Core HR
priority: high
type: security
status: draft
created: 2026-06-11
---

# TC-CHR-019: Input sanitization -- XSS and SQL injection in department name

## 1. Test Objective
Verify that department name and description fields are sanitized against XSS and SQL injection attacks, and that malicious input is either rejected or safely stored/rendered without execution.

## 2. Related Requirements
- User Story: US-CHR-004
- Functional Requirements: FR-1
- Non-Functional Requirements: NFR-2 (tenant-isolated, secure data handling)

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| XSS payload (name) | `<script>alert('xss')</script>` | Script injection |
| XSS payload (description) | `<img src=x onerror=alert(1)>` | Event handler injection |
| SQL injection (name) | `'; DROP TABLE department; --` | SQL injection attempt |
| Encoded XSS | `%3Cscript%3Ealert(1)%3C/script%3E` | URL-encoded script tag |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/departments` with name = `<script>alert('xss')</script>` | Either: (a) API returns 400 with validation error rejecting HTML/script tags, or (b) input is stored as plain text and the response HTML-encodes it. No script execution. |
| 2 | If the department was created, navigate to the department list and verify the name is rendered as plain text | The script tag is displayed as text, not executed. No alert dialog appears. |
| 3 | Send `POST /api/v1/departments` with description = `<img src=x onerror=alert(1)>` | Same behavior: rejected or safely escaped. No JavaScript execution. |
| 4 | Send `POST /api/v1/departments` with name = `'; DROP TABLE department; --` | API uses parameterized queries (EF Core). The name is either stored as literal text or rejected for invalid characters. No SQL execution. Verify the `department` table still exists and is intact. |
| 5 | Verify the department table is unaffected by the SQL injection attempt | All existing records are present. No data loss or corruption. |

## 6. Postconditions
- No XSS payloads execute in the browser.
- No SQL injection affects the database.
- System remains in a consistent state.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
