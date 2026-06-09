---
id: TC-AUTH-058
user_story: US-AUTH-007
module: Authentication
priority: medium
type: accessibility
status: draft
created: 2026-06-09
---

# TC-AUTH-058: Static 404 and suspended workspace pages meet accessibility and information disclosure rules

## 1. Test Objective
Verify that the unknown-workspace and suspended-tenant pages are accessible, keyboard usable, and do not expose the SPA shell, login form, API details, or internal platform information.

## 2. Related Requirements
- User Story: US-AUTH-007
- Acceptance Criteria: AC-2, AC-5
- Functional Requirements: FR-7, FR-8
- Non-Functional Requirements: NFR-5
- UI/UX Notes: Static 404 page and suspension notice page

## 3. Preconditions
- No tenant exists for `unknown.yourhrm.com`.
- Tenant `suspcorp` exists with status `suspended`, name, branding, and suspension reason.
- Browser-based accessibility tooling is available for WCAG 2.1 AA checks.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Unknown host | unknown.yourhrm.com | Static 404 view |
| Suspended host | suspcorp.yourhrm.com | Suspended workspace view |
| Viewports | 360px, 768px, 1440px | Responsive accessibility checks |
| Assistive checks | Keyboard, screen reader names, contrast | WCAG 2.1 AA |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `https://unknown.yourhrm.com/` at each target viewport. | Static 404 page renders with clear "This workspace does not exist" messaging and no SPA shell. |
| 2 | Inspect DOM and network requests for the unknown workspace page. | No `<app-root>`, login form, SPA bundle, API endpoint list, stack trace, version number, or internal service name is exposed. |
| 3 | Run keyboard navigation on the unknown workspace page. | Focus order reaches the main platform link and any support link without traps. |
| 4 | Run automated and manual accessibility checks on the unknown workspace page. | Page has one logical heading, accessible link names, visible focus indicators, and WCAG 2.1 AA color contrast. |
| 5 | Navigate to `https://suspcorp.yourhrm.com/` at each target viewport. | Branded suspension page renders with tenant name, suspension reason when available, and contact support link. |
| 6 | Inspect DOM and network requests for the suspended page. | Standard login form is not displayed; no token issuance endpoints are exposed through the page. |
| 7 | Run keyboard and screen reader checks on the suspended page. | Heading, reason text, and support link are perceivable and operable without pointer input. |
| 8 | Verify responsive behavior at 360px width. | Content does not overlap or require horizontal scrolling. |

## 6. Postconditions
- Static 404 and suspended pages remain accessible and safe from information disclosure.
- No authentication tokens are issued during UI checks.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [x] Cross-browser test
