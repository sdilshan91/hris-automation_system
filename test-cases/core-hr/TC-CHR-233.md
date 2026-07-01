---
id: TC-CHR-233
user_story: US-CHR-009
module: Core HR
priority: high
type: functional
status: blocked
created: 2026-06-12
---

# TC-CHR-233: Status badge is color-coded on employee profile and directory per FR-7

## 1. Test Objective
Verify that the employee status is displayed as a color-coded pill-shaped badge on both the employee profile header and the employee directory. Each status maps to the correct Tailwind color class. This validates FR-7 and the badge color mapping.

## 2. Related Requirements
- User Story: US-CHR-009
- Functional Requirements: FR-7

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated in the "acme" tenant context.
- Employees exist with each of the five statuses: active, probation, suspended, terminated, inactive.

## 4. Test Data
| Status | Expected Color | Tailwind Class |
|--------|---------------|----------------|
| active | Green | `bg-green-100 text-green-800` |
| probation | Amber | `bg-amber-100 text-amber-800` |
| suspended | Gray | `bg-gray-100 text-gray-800` |
| terminated | Red | `bg-red-100 text-red-800` |
| inactive | Slate | `bg-slate-100 text-slate-800` |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the profile of the employee with status `active`. | Profile header shows a pill-shaped badge with text "Active" in green (bg-green-100 text-green-800). |
| 2 | Navigate to the profile of the employee with status `probation`. | Badge shows "Probation" in amber (bg-amber-100 text-amber-800). |
| 3 | Navigate to the profile of the employee with status `suspended`. | Badge shows "Suspended" in gray (bg-gray-100 text-gray-800). |
| 4 | Navigate to the profile of the employee with status `terminated`. | Badge shows "Terminated" in red (bg-red-100 text-red-800). |
| 5 | Navigate to the profile of the employee with status `inactive`. | Badge shows "Inactive" in slate (bg-slate-100 text-slate-800). |
| 6 | Navigate to the employee directory. | Each employee card/row shows the appropriate color-coded status badge matching their current status. |
| 7 | Inspect the DOM for badge elements. | Each badge element has the correct Tailwind classes applied. Badge is pill-shaped (rounded-full or similar). |

## 6. Postconditions
- No data changes. This test verifies display only.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

> **Execution 2026-06-30 (FE, acme):** STILL BLOCKED — status badge color-coding shows on the employee profile + directory, both unreachable/crashed in-app (**BUG-099** — directory list never renders). No in-app path to verify the badge.

> **Execution 2026-07-01 (triage, acme):** STILL BLOCKED — FE-UI-only arm (responsive-viewport / cross-browser / visual-render). Not API-testable this pass; requires viewport resizing (360px–1920px) and/or multiple browser engines the single shared MCP session can't drive. Not a functional/business-rule defect.
