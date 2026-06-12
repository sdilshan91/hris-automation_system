---
id: TC-CHR-236
user_story: US-CHR-009
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-236: Cross-browser compatibility -- status change flow works on Chrome, Edge, Firefox, Safari

## 1. Test Objective
Verify that the status change workflow (form rendering, submission, badge updates, timeline display) functions correctly across all supported browsers: Chrome (latest), Edge (latest), Firefox (latest), and Safari (latest). This validates cross-browser compatibility.

## 2. Related Requirements
- User Story: US-CHR-009
- Non-Functional Requirements: NFR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated in the "acme" tenant context.
- Employee "John Smith" (`emp-001-uuid`) exists with status `active`.
- Test machines or BrowserStack/Sauce Labs available for all 4 browsers.

## 4. Test Data
| Browser | Version | Platform |
|---------|---------|----------|
| Chrome | Latest stable | Windows 11 / macOS |
| Edge | Latest stable | Windows 11 |
| Firefox | Latest stable | Windows 11 / macOS |
| Safari | Latest stable | macOS |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | In each browser: Navigate to the employee profile. | Profile loads correctly. Status badge renders with correct color. "Change Status" button is visible. |
| 2 | Click "Change Status". | Modal opens correctly. Form fields render (dropdown, textarea, date picker). No layout issues. |
| 3 | Complete the status change form and submit. | Confirmation dialog appears. After confirm: success toast, badge updates, modal closes. |
| 4 | Verify the employment history timeline renders correctly. | Timeline entries display with correct layout, colors, and content in all browsers. |
| 5 | Verify the badge color transition animation (200ms ease). | Animation plays smoothly in all browsers. |
| 6 | Test at 360px viewport in each browser. | Bottom sheet renders correctly. No layout breaks or overflow. |

## 6. Postconditions
- Status change verified as functional in all 4 supported browsers.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test
