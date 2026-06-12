---
id: TC-CHR-266
user_story: US-CHR-010
module: Core HR
priority: high
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-266: Input sanitization -- XSS payload in import CSV field values does not execute

## 1. Test Objective
Verify that malicious content (XSS payloads) embedded in import file field values (e.g., first_name, last_name) is sanitized and stored safely. The payload must not execute when the imported employee data is displayed in the directory or profile views.

## 2. Related Requirements
- User Story: US-CHR-010
- Functional Requirements: FR-3 (validation)
- Non-Functional Requirements: NFR-2 (security -- tenant isolation and data integrity)

## 3. Preconditions
- Tenant "acme" exists with status `active` and sufficient capacity.
- An HR Officer user is authenticated.
- Required departments and job titles exist.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Authorized persona |
| File Name | xss_test.csv | 2 rows with XSS payloads |
| Row 1 first_name | `<script>alert('xss')</script>` | Reflected XSS attempt |
| Row 2 last_name | `"><img src=x onerror=alert(1)>` | Attribute injection attempt |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Upload `xss_test.csv` and click "Import". | System processes the file. Rows are either imported (with sanitized values) or rejected if the validation rejects special characters. |
| 2 | If imported, navigate to the employee directory and locate the imported records. | The employee names display as escaped text (e.g., `&lt;script&gt;alert('xss')&lt;/script&gt;`). No JavaScript executes. No alert dialog appears. |
| 3 | View the employee profile for row 1's record. | The `first_name` field shows the literal text, not rendered HTML. |
| 4 | Inspect the HTML source of the directory/profile page. | XSS payload is HTML-encoded in the DOM, not injected as executable markup. |
| 5 | Check the API response for the employee record. | The stored value is either sanitized or the original literal string (Angular template binding escapes by default). |

## 6. Postconditions
- No XSS execution occurs. Data is safely stored and displayed.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
