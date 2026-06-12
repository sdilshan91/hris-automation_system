---
id: TC-CHR-122
user_story: US-CHR-002
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-122: Responsive design -- profile page at 360px, 768px, and 1440px

## 1. Test Objective
Verify that the employee profile page is fully responsive and renders correctly at mobile (360px), tablet (768px), and desktop (1440px) breakpoints. Cards should stack vertically on mobile, the avatar should shrink, and tabs should collapse to a dropdown. This validates NFR-5.

## 2. Related Requirements
- User Story: US-CHR-002
- Non-Functional Requirements: NFR-5
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme" tenant.
- Employee "Jane Doe" exists with populated profile.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Full access |
| Employee ID | {jane_doe_id} | Populated profile |
| Viewport 1 | 360px width | Mobile |
| Viewport 2 | 768px width | Tablet |
| Viewport 3 | 1440px width | Desktop |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Set viewport to 360px width (mobile) | Page reflows to single-column layout. |
| 2 | Verify cards at 360px | All section cards stack vertically, full-width. No horizontal overflow or scrollbar. |
| 3 | Verify avatar at 360px | Avatar shrinks from 96px to 64px. |
| 4 | Verify tab navigation at 360px | MatTabGroup collapses into a dropdown selector (or equivalent mobile-friendly navigation). |
| 5 | Verify edit functionality at 360px | Edit buttons are accessible. Edit mode renders inputs within the full-width card. Save/Cancel buttons are visible without scrolling horizontally. |
| 6 | Set viewport to 768px width (tablet) | Layout adjusts to tablet-friendly view -- possibly 2-column cards or wider single column. |
| 7 | Verify cards at 768px | Cards have appropriate width; no content truncation. Employment history timeline is readable. |
| 8 | Verify tab navigation at 768px | Tabs are visible (not collapsed to dropdown) if space permits, or appropriately scrollable. |
| 9 | Set viewport to 1440px width (desktop) | Full desktop layout with MatTabGroup tab indicator animation. |
| 10 | Verify cards at 1440px | Cards use the `rounded-xl shadow-sm bg-white` styling. Multi-column layout where appropriate. Summary header shows full-size 96px avatar. |
| 11 | Verify no content overflow at any breakpoint | No horizontal scrollbar appears. All text is readable without zooming. |

## 6. Postconditions
- Profile page renders correctly at all three breakpoints.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test
