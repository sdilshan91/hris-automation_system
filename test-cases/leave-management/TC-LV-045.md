---
id: TC-LV-045
user_story: US-LV-002
module: Leave Management
priority: medium
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-045: Cross-browser compatibility for entitlement configuration page

## 1. Test Objective
Verify that the entitlement configuration page (matrix view, forms, inline editing, Notion-like database view) renders correctly and functions identically across Chrome, Edge, Firefox, and Safari.

## 2. Related Requirements
- User Story: US-LV-002
- UI/UX Notes: Section 8

## 3. Preconditions
- Tenant "acme" exists with entitlement rules configured.
- A user with `Leave.Configure` permission is authenticated.
- Test environment accessible from all target browsers.

## 4. Test Data
| Browser | Version | Platform |
|---------|---------|----------|
| Chrome | Latest stable | Windows 11, macOS |
| Edge | Latest stable | Windows 11 |
| Firefox | Latest stable | Windows 11, macOS |
| Safari | Latest stable | macOS |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Open entitlement configuration page in Chrome | Page loads correctly. Matrix renders with proper alignment and spacing. |
| 2 | Create a new entitlement rule in Chrome | Rule created successfully. Form fields, dropdowns, and validation work. |
| 3 | Edit an entitlement rule inline in Chrome | Inline editing works with proper save behavior. |
| 4 | Repeat steps 1-3 in Edge | Identical behavior and rendering. |
| 5 | Repeat steps 1-3 in Firefox | Identical behavior and rendering. |
| 6 | Repeat steps 1-3 in Safari | Identical behavior and rendering. |
| 7 | Verify Notion-like filter/sort/group controls work in all browsers | Database view interactions (filter by department, sort by days, group by leave type) work consistently. |
| 8 | Verify the per-employee override form works in all browsers | Override creation from employee profile Leave tab works in all browsers. |

## 6. Postconditions
- Entitlement configuration is fully functional in all four target browsers.
- No browser-specific rendering issues.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [x] Cross-browser test
