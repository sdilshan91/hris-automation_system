---
id: TC-LV-015
user_story: US-LV-001
module: Leave Management
priority: high
type: security
status: draft
created: 2026-06-13
---

# TC-LV-015: Input sanitization -- XSS in leave type name and description

## 1. Test Objective
Verify that the system sanitizes user input to prevent Cross-Site Scripting (XSS) attacks in leave type name, description, and other text fields. Malicious script payloads must be escaped or rejected.

## 2. Related Requirements
- User Story: US-LV-001
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists.
- A user with `Leave.Configure` permission is authenticated.

## 4. Test Data
| Field | Malicious Input | Notes |
|-------|----------------|-------|
| Name | `<script>alert('xss')</script>` | Script injection |
| Description | `<img src=x onerror=alert(1)>` | Image tag injection |
| Code | `"; DROP TABLE leave_type; --` | SQL injection attempt |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/leave-types` with name = `<script>alert('xss')</script>` | API either rejects with 400 (invalid characters) or accepts and stores the value as escaped/sanitized HTML entities. No script execution on retrieval. |
| 2 | Send `POST /api/v1/leave-types` with description = `<img src=x onerror=alert(1)>` | Same behavior: rejected or safely escaped. |
| 3 | If records were created, retrieve them via GET and render in UI | Verify that no JavaScript executes. Values are displayed as plain text or escaped HTML. |
| 4 | Send `POST /api/v1/leave-types` with code = `"; DROP TABLE leave_type; --` | API rejects with validation error (invalid code format) or safely parameterizes the query. No SQL injection occurs. |
| 5 | Verify the `leave_type` table is intact | `SELECT count(*) FROM leave_type` returns expected count. Table is not dropped or corrupted. |

## 6. Postconditions
- No XSS or SQL injection vulnerabilities exploited.
- Database integrity maintained.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
