---
id: TC-CHR-048
user_story: US-CHR-005
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-048: Deactivated job titles hidden from assignment dropdowns, visible in admin

## 1. Test Objective
Verify that deactivated job titles are not shown in employee assignment dropdowns (or any selection context where new assignments are made) but remain visible in the Job Titles admin management page, per FR-5 and BR-3. Note: The actual employee assignment dropdown is deferred until US-CHR-001 is built; this test validates the API filter behavior for active-only lists vs. admin lists.

## 2. Related Requirements
- User Story: US-CHR-005
- Functional Requirements: FR-5
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with Tenant Admin role is authenticated in the "acme" tenant context.
- A job title "Legacy Coordinator" exists in the "acme" tenant with `is_active = false`.
- An active job title "Software Engineer" also exists.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | Tenant Admin | Authorized role |
| Active Title | Software Engineer | is_active = true |
| Inactive Title | Legacy Coordinator | is_active = false |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET /api/v1/job-titles?active_only=true` (or equivalent filter for assignment dropdowns) | Response contains "Software Engineer" but does NOT contain "Legacy Coordinator". |
| 2 | Call `GET /api/v1/job-titles` (admin list, no active filter or with `include_inactive=true`) | Response contains both "Software Engineer" and "Legacy Coordinator". |
| 3 | Navigate to the Job Titles admin management page in the UI | Both titles are visible. "Legacy Coordinator" is shown with an "Inactive" status indicator. |
| 4 | If an employee form/dropdown is available (future), open it and verify the job title dropdown | "Legacy Coordinator" is NOT in the dropdown options. "Software Engineer" IS in the dropdown options. (This step deferred to US-CHR-001.) |

## 6. Postconditions
- No data is modified; this is a read-only verification.
- Deactivated titles remain in the database and are accessible to admin views.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
