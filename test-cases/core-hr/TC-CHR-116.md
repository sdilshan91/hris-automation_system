---
id: TC-CHR-116
user_story: US-CHR-002
module: Core HR
priority: high
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-116: Input sanitization -- XSS and SQL injection in profile edit fields

## 1. Test Objective
Verify that profile edit fields are properly sanitized against XSS and SQL injection attacks. Malicious input should be either rejected or safely escaped/stored without execution. This validates security requirements.

## 2. Related Requirements
- User Story: US-CHR-002
- Functional Requirements: FR-2, FR-3
- Non-Functional Requirements: NFR-3 (tenant isolation via safe queries)

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme" tenant.
- Employee "Jane Doe" exists.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| XSS payload (phone) | `<script>alert('xss')</script>` | Script injection |
| XSS payload (address) | `<img src=x onerror=alert(1)>` | Image-based XSS |
| SQL injection (phone) | `'; DROP TABLE employees; --` | SQL injection |
| SQL injection (address) | `1' OR '1'='1` | SQL injection |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `PATCH /api/v1/tenant/employees/{jane_doe_id}` with `{ "phone": "<script>alert('xss')</script>", "xmin": "..." }` | Response is 400 Bad Request (validation rejects) OR 200 OK with the string stored safely as escaped text. No script execution on page render. |
| 2 | If stored, navigate to Jane Doe's profile | The phone field displays the literal text `<script>alert('xss')</script>` without executing the script. |
| 3 | Send `PATCH` with `{ "address": "<img src=x onerror=alert(1)>", "xmin": "..." }` | Same behavior as step 1 -- rejected or safely escaped. |
| 4 | Send `PATCH` with `{ "phone": "'; DROP TABLE employees; --", "xmin": "..." }` | The SQL is not executed. The phone field stores the literal string. The employees table remains intact. |
| 5 | Verify the employees table exists and contains all records | `SELECT count(*) FROM employees` returns the correct count. No data loss. |
| 6 | Send `PATCH` with `{ "address": "1' OR '1'='1", "xmin": "..." }` | Stored as literal string; no SQL injection executed. |

## 6. Postconditions
- No XSS payloads are executable in the UI.
- No SQL injection was executed.
- All data remains intact.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
