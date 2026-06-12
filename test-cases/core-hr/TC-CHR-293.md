---
id: TC-CHR-293
user_story: US-CHR-011
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-293: Responsive layout at 360px -- manager selector overlay and My Team stack

## 1. Test Objective
Verify that the manager assignment UI is fully responsive at 360px viewport width. The manager selector should appear as a full-screen search overlay on mobile, and the "My Team" cards should stack vertically. Bulk actions should be accessible via a bottom sheet. This validates NFR-4.

## 2. Related Requirements
- User Story: US-CHR-011
- Non-Functional Requirements: NFR-4
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer or Manager user is authenticated.
- Browser DevTools set to 360px viewport width or tested on a physical mobile device.
- Employee E exists. Manager M has 3 direct reports.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Viewport | 360px width | Mobile simulation |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Set viewport to 360px. Navigate to Employee E's profile, Employment Details section. | The page renders correctly at 360px. The Reporting Manager field is visible and accessible. |
| 2 | Click edit on the Reporting Manager field. | A full-screen search overlay opens (not a desktop-sized modal). The overlay fills the screen with a search input at the top and results list below. |
| 3 | Search for a manager name. | Results list is scrollable. Each result shows avatar, name, department in a stacked layout. |
| 4 | Select a result and confirm. | The overlay closes. The profile returns to view mode with the updated manager. |
| 5 | Navigate to the My Team view (as Manager M). | Direct-report cards stack vertically in a single column. Each card is full width. |
| 6 | Verify card content at 360px. | Name, job title, department, and status badge are visible. No horizontal overflow or truncation of critical information. |
| 7 | Navigate to the employee directory and select multiple employees. | Bulk action controls appear as a bottom sheet (not a floating toolbar that obscures content). |
| 8 | Verify smooth animations on card transitions. | Animations execute at 200ms ease without jank. |

## 6. Postconditions
- No state change; UI responsiveness verification only.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test
