---
id: TC-CHR-310
user_story: US-CHR-012
module: Core HR
priority: high
type: security
status: blocked
exec_note: "2026-07-01 (API, fntest): BLOCKED (partial-verified) — API arm SAFE: custom-field name '<script>x</script>' stored+returned as a literal string (201, JSON-escaped, no server-side execution). The remaining 'HTML-encoded in DOM / no script runs' assertion is an FE Angular auto-escape render check needing a browser on the custom-fields page (blocked by BUG-097). No API-layer injection defect."
created: 2026-06-13
---

# TC-CHR-310: Input sanitization -- XSS in custom field name and dropdown options

## 1. Test Objective
Verify that malicious scripts injected into custom field names, dropdown options, or field values are sanitized and do not execute in the browser. This protects against stored XSS attacks.

## 2. Related Requirements
- User Story: US-CHR-012
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- Tenant Admin is authenticated.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Malicious Field Name | `<script>alert('xss')</script>` | XSS payload |
| Malicious Option | `<img src=x onerror=alert(1)>` | XSS payload |
| Malicious Value | `"><script>document.cookie</script>` | XSS payload |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Attempt to create a custom field with name `<script>alert('xss')</script>`. | Either the name is rejected with a validation error, or it is stored but HTML-encoded on render. No script executes. |
| 2 | Create a dropdown field with option `<img src=x onerror=alert(1)>`. | The option is stored safely. When rendered on the management page or employee form, no image load or script execution occurs. |
| 3 | Set a text-type custom field value to `"><script>document.cookie</script>` on an employee record. | The value is stored in JSONB. When displayed on the profile page, the text is HTML-encoded and no script runs. |
| 4 | Inspect the rendered HTML in the browser DevTools. | All user-supplied strings are properly escaped/encoded in the DOM. |

## 6. Postconditions
- No XSS payloads execute in the browser.
- Data is stored safely and rendered with proper encoding.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

> **Execution 2026-06-30 (FE, acme):** STILL BLOCKED — the custom-field create modal cannot open (**BUG-100**: openAddModal throws), so an XSS payload cannot be entered into a field name / dropdown option via the UI. Server-side sanitization would need an API probe regardless.
