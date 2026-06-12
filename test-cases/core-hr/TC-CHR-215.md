---
id: TC-CHR-215
user_story: US-CHR-008
module: Core HR
priority: high
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-215: Input sanitization -- XSS in document description field

## 1. Test Objective
Verify that the document description field sanitizes user input to prevent cross-site scripting (XSS) attacks. A description containing `<script>` tags or other XSS payloads must be stored safely (escaped/sanitized) and rendered without executing any embedded scripts.

## 2. Related Requirements
- User Story: US-CHR-008
- Functional Requirements: FR-1
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- Employee "Jane Doe" (emp-001-uuid) exists.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| File | legit-doc.pdf | 1 MB, valid PDF |
| Description (XSS) | `<script>alert('XSS')</script>` | XSS payload |
| Description (img XSS) | `<img src=x onerror=alert('XSS')>` | Event handler XSS |
| Category | Other | Valid category |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Upload "legit-doc.pdf" with description `<script>alert('XSS')</script>`. | Upload succeeds (the description is stored safely, not rejected). |
| 2 | Navigate to the Documents tab and view the document list. | The description text is displayed as literal escaped text: `<script>alert('XSS')</script>`. No JavaScript alert executes. |
| 3 | Inspect the rendered HTML in browser DevTools. | The `<script>` tag is HTML-escaped (e.g., `&lt;script&gt;`) or stripped entirely. It is NOT present as executable markup. |
| 4 | Upload another document with description `<img src=x onerror=alert('XSS')>`. | Upload succeeds. |
| 5 | View the document list. | The description is rendered as escaped text. No `onerror` event fires. No JavaScript alert executes. |
| 6 | Verify the stored value in the database. | The `description` column contains the raw input (the API accepts it) but the frontend renders it safely via Angular's built-in XSS protection (template binding auto-escapes). |

## 6. Postconditions
- No XSS payload executed in the browser.
- Descriptions are stored and rendered safely.
- Angular's default output encoding prevents script injection.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
