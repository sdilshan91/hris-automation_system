---
id: TC-CHR-125
user_story: US-CHR-002
module: Core HR
priority: medium
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-125: Soft-deleted employee not visible in normal views; accessible via Archived filter for HR

## 1. Test Objective
Verify that soft-deleted employees are hidden from normal employee profile views and lists, but HR Officers can access them using an "Archived" filter. This validates BR-6.

## 2. Related Requirements
- User Story: US-CHR-002
- Business Rules: BR-6

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- HR Officer is authenticated in "acme".
- Employee "Departed Dave" (EMP-0099) exists and has been soft-deleted (is_deleted = true).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Full access |
| Employee ID | {departed_dave_id} | Soft-deleted |
| Employee No | EMP-0099 | Archived employee |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to employee list page without any archive filter | "Departed Dave" does NOT appear in the list. |
| 2 | Send `GET /api/v1/tenant/employees?activeOnly=true` | "Departed Dave" is not in the response. |
| 3 | Send `GET /api/v1/tenant/employees/{departed_dave_id}` | Response is 404 Not Found (EF global query filter excludes soft-deleted). |
| 4 | Apply the "Archived" filter on the employee list page (if available) | "Departed Dave" appears in the filtered list. |
| 5 | Send `GET /api/v1/tenant/employees?includeArchived=true` (or equivalent parameter) | "Departed Dave" appears in the response with `is_deleted: true`. |

## 6. Postconditions
- Soft-deleted employees are hidden by default but accessible via archive filter.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
