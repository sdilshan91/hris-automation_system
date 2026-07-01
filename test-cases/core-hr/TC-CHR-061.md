---
id: TC-CHR-061
user_story: US-CHR-005
module: Core HR
priority: medium
type: functional
status: blocked
exec_note: "2026-07-01: BLOCKED — FE-UI-only arm (responsive-viewport 360px–1920px / pan-zoom / visual-render / cross-browser). Not API-testable; the single shared chromium MCP session cannot exercise a viewport matrix or multiple browser engines, and in-app SPA nav is blocked (systemic BUG-097). Not a functional/business-rule defect."
created: 2026-06-12
---

# TC-CHR-061: Job titles management UI responsive design (360px to 1920px)

## 1. Test Objective
Verify that the Job Titles management page is fully responsive across viewport widths from 360px (mobile) to 1920px (desktop), adapting layout and interaction patterns as specified in the UI/UX notes (table becomes card list on mobile).

## 2. Related Requirements
- User Story: US-CHR-005
- Non-Functional Requirements: NFR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated.
- Multiple job titles exist in the tenant.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Viewport 1 | 360px width | Mobile (small phone) |
| Viewport 2 | 768px width | Tablet portrait |
| Viewport 3 | 1024px width | Tablet landscape / small desktop |
| Viewport 4 | 1920px width | Full HD desktop |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Set viewport to 1920px and navigate to Job Titles page | Full table layout with all columns (Title Name, Grade, Employee Count, Status, Actions) visible. Card container has rounded-xl shadow-sm. |
| 2 | Set viewport to 1024px | Table layout is maintained; columns may slightly compress but remain readable. |
| 3 | Set viewport to 768px | Table may switch to a more compact layout or card list. All data remains accessible. |
| 4 | Set viewport to 360px (mobile) | Table becomes a card list with stacked fields per UI/UX notes. Each card shows Title Name, Grade, Employee Count, Status. Action buttons are accessible. |
| 5 | On 360px viewport, click "Add Job Title" | Modal/slide-over panel opens and is usable within the small viewport. Form fields are not cut off. |
| 6 | On 360px viewport, verify search bar is accessible | Search bar is visible and functional. |
| 7 | Verify no horizontal scrollbar appears at any viewport width | Content adapts without requiring horizontal scrolling. |
| 8 | Verify touch targets are at least 44x44px on mobile viewports | Buttons and interactive elements meet minimum touch target size. |

## 6. Postconditions
- The page renders correctly at all tested viewport widths.
- No layout breakage or content overflow detected.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [x] Accessibility test
- [x] Cross-browser test

> **Execution 2026-06-30 (FE, acme):** STILL BLOCKED — responsive/cross-browser TC. Job Titles renders correctly at the current desktop viewport; requires viewport resizing / multiple browser engines not available in the fixed single shared chromium MCP session.

> **Execution 2026-07-01 (triage, acme):** STILL BLOCKED — FE-UI-only arm (responsive-viewport / cross-browser / visual-render). Not API-testable this pass; requires viewport resizing (360px–1920px) and/or multiple browser engines the single shared MCP session can't drive. Not a functional/business-rule defect.
