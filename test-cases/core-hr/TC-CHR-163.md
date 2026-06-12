---
id: TC-CHR-163
user_story: US-CHR-006
module: Core HR
priority: high
type: security
status: draft
created: 2026-06-12
---

# TC-CHR-163: Input sanitization -- XSS in org tree search bar

## 1. Test Objective
Verify that the org tree search bar sanitizes input to prevent Cross-Site Scripting (XSS) attacks. Script tags and malicious input should be treated as literal text, not executed. This is a security test for FR-4.

## 2. Related Requirements
- User Story: US-CHR-006
- Functional Requirements: FR-4
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in the "acme" tenant context.
- Org chart is rendered.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| XSS Payload 1 | `<script>alert('xss')</script>` | Classic script injection |
| XSS Payload 2 | `<img src=x onerror=alert('xss')>` | Image tag injection |
| XSS Payload 3 | `"; DROP TABLE departments; --` | SQL injection attempt |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Organization Tree page | Org chart renders. |
| 2 | Type `<script>alert('xss')</script>` in the search bar | No JavaScript alert is triggered. The text is displayed literally in the search bar. |
| 3 | Verify the API request | The search query is URL-encoded in the API call. The response does not execute any scripts. |
| 4 | Verify the typeahead dropdown | If "no results" is displayed, the XSS payload is rendered as escaped text, not as HTML. |
| 5 | Clear the search bar and type `<img src=x onerror=alert('xss')>` | No JavaScript alert is triggered. No image element is injected into the DOM. |
| 6 | Clear the search bar and type `"; DROP TABLE departments; --` | The search returns "no results" or handles the input gracefully. No SQL error or data modification occurs. |
| 7 | Verify no console errors related to injection | Browser console shows no unexpected script execution warnings. |

## 6. Postconditions
- No data was modified or exposed.
- No scripts were executed.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
