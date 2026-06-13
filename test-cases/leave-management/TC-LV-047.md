---
id: TC-LV-047
user_story: US-LV-002
module: Leave Management
priority: high
type: security
status: draft
created: 2026-06-13
---

# TC-LV-047: Input sanitization -- XSS in entitlement rule and override fields

## 1. Test Objective
Verify that entitlement rule and override API endpoints properly sanitize all text inputs to prevent cross-site scripting (XSS) attacks. Malicious payloads in text fields must be escaped or rejected.

## 2. Related Requirements
- User Story: US-LV-002
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with `Leave.Configure` permission is authenticated.

## 4. Test Data
| Field | XSS Payload | Expected Behavior |
|-------|------------|-------------------|
| Override reason | `<script>alert('xss')</script>` | Escaped or rejected |
| Override reason | `"><img src=x onerror=alert(1)>` | Escaped or rejected |
| Override reason | `javascript:alert(document.cookie)` | Escaped or rejected |
| Employment type (if free-text) | `<svg onload=alert(1)>` | Escaped or rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create a per-employee override with reason = `<script>alert('xss')</script>` | If accepted: the value is stored HTML-encoded. If rejected: 400 Bad Request with sanitization error. |
| 2 | Retrieve the override via API | The reason field contains escaped HTML (e.g., `&lt;script&gt;`) or the sanitized plain text, never raw HTML. |
| 3 | View the override reason in the UI | No script execution occurs. The text is rendered as literal characters. |
| 4 | Attempt override with reason = `"><img src=x onerror=alert(1)>` | Same behavior: escaped or rejected. No image tag rendered. |
| 5 | Attempt override with reason = `javascript:alert(document.cookie)` | Stored as plain text if accepted. No script execution when displayed. |
| 6 | Verify the entitlement matrix UI does not render unsanitized HTML in any cell | Inspect the DOM: no raw HTML elements from user input are present in the rendered matrix. |

## 6. Postconditions
- No XSS payloads are executable in the application.
- All user-supplied text is properly escaped in both API responses and UI rendering.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
