---
id: TC-CHR-288
user_story: US-CHR-011
module: Core HR
priority: high
type: security
status: pass
exec_note: "2026-07-03 (FE, acme; BUG-099 fix unblocked profile reach): PASS — FE-render arm now exercised end-to-end. Router-nav to /employees/019efced-… → Employment tab → 'Change reporting manager' opens the manager-selector modal. Typed payloads `<script>alert('xss')</script>` and `\"><img src=x onerror=alert(1)>` into the search field (native value-setter + input event): NO alert fired (window.alert hook not called), NO <script> tag injected, NO <img src=x> element injected into the DOM; the dialog showed 'No active employees found.' — payload treated as a literal, non-matching query, not reflected as HTML (Angular default escaping). Combined with the previously-verified SAFE API arm (2026-07-01: JSON-escaped, application/json, no HTML reflection) → full PASS. Prior BUG-097 FE-block superseded. (Note a11y gaps on this same modal tracked separately as ISSUE-237.)"
created: 2026-06-12
---

# TC-CHR-288: Input sanitization -- XSS in manager search autocomplete

## 1. Test Objective
Verify that the manager search/autocomplete field sanitizes user input to prevent cross-site scripting (XSS) attacks. Malicious scripts entered in the search field should not execute in the browser.

## 2. Related Requirements
- User Story: US-CHR-011
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated.
- An employee profile is open for editing the Reporting Manager field.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| XSS payload 1 | `<script>alert('xss')</script>` | Classic script injection |
| XSS payload 2 | `"><img src=x onerror=alert(1)>` | Attribute-based injection |
| XSS payload 3 | `javascript:alert(document.cookie)` | URI scheme injection |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open the manager selector modal/autocomplete. | The search field is displayed. |
| 2 | Enter XSS payload 1 into the search field. | No alert dialog appears. The text is displayed as literal text or sanitized. Search returns no results (no employee name matches). |
| 3 | Enter XSS payload 2 into the search field. | No image error handler executes. Input is sanitized. |
| 4 | Enter XSS payload 3 into the search field. | No JavaScript execution occurs. |
| 5 | Send API request with XSS payload in the search query parameter. | The API returns results (or empty set) without reflecting unsanitized HTML. Response content-type is application/json with escaped values. |

## 6. Postconditions
- No XSS executed. No state change.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

> **Execution 2026-06-30 (FE, acme):** STILL BLOCKED — the manager-search autocomplete is on the employee edit form, gated by the crashed directory (**BUG-099**); XSS input can't be entered via UI. Server-side sanitization needs an API probe.
