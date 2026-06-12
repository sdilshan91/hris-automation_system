---
id: TC-CHR-035
user_story: US-CHR-005
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-035: Job Titles list page displays correct columns and layout

## 1. Test Objective
Verify that a Tenant Admin navigating to the Job Titles management page sees a list/table of existing job titles with the columns specified in AC-1: Title Name, Grade (if linked), Employee Count, Status, and action buttons.

## 2. Related Requirements
- User Story: US-CHR-005
- Acceptance Criteria: AC-1
- Functional Requirements: FR-1
- Non-Functional Requirements: NFR-3
- Business Rules: BR-4

## 3. Preconditions
- Tenant "acme" exists with status `active` and subdomain `acme.yourhrm.com`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- At least two job titles exist in the "acme" tenant: one with a linked grade and one without.
- At least one job title has `is_active = false` (deactivated).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Authorized role |
| Job Title 1 | Software Engineer | Active, linked to Grade "L4" |
| Job Title 2 | Office Assistant | Active, no grade linked |
| Job Title 3 | Legacy Coordinator | Deactivated |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to the Job Titles management page at `https://acme.yourhrm.com/job-titles` | Job Titles list page loads with a card-based table (rounded-xl shadow-sm container). |
| 2 | Verify the table/list header columns | Columns visible: "Title Name", "Grade", "Employee Count", "Status", and an actions column. |
| 3 | Verify the "Software Engineer" row | Title Name: "Software Engineer", Grade: "L4", Employee Count: a numeric badge, Status: "Active" (or active indicator). |
| 4 | Verify the "Office Assistant" row | Title Name: "Office Assistant", Grade: "-" or empty, Employee Count: a numeric badge (0 or more), Status: "Active". |
| 5 | Verify the "Legacy Coordinator" row is visible in the admin view | Title Name: "Legacy Coordinator", Status: "Inactive" (visually distinct from active entries). |
| 6 | Verify action buttons are present on each row | Edit and Deactivate action icons are visible on hover for each row. |
| 7 | Verify the "Add Job Title" button is present at the top-right of the page | Button is visible and labelled "Add Job Title" (or has a `+` icon with accessible label). |
| 8 | Verify a search bar is present at the top of the table | Search bar is visible and functional. |

## 6. Postconditions
- No data is modified; this is a read-only verification.
- The page remains in a stable state for further interactions.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
