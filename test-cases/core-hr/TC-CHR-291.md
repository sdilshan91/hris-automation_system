---
id: TC-CHR-291
user_story: US-CHR-011
module: Core HR
priority: high
type: performance
status: draft
created: 2026-06-12
---

# TC-CHR-291: Bulk manager assignment for 100 employees completes within 5 seconds

## 1. Test Objective
Verify that a bulk manager assignment operation for 100 employees completes within 5 seconds, including individual audit entry creation for each employee. This validates NFR-6.

## 2. Related Requirements
- User Story: US-CHR-011
- Non-Functional Requirements: NFR-6
- Functional Requirements: FR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated.
- 100 employees exist with no manager assigned.
- Manager M exists with status `active`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employees | bulk1@acme.test through bulk100@acme.test | 100 employees |
| Manager M | bulk.mgr@acme.test | Target manager |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send a bulk manager assignment API request for all 100 employees to Manager M. Record the total response time. | Operation completes successfully. |
| 2 | Verify the total response time. | Total time is <= 5 seconds. |
| 3 | Verify all 100 employees have `reports_to_employee_id` = M.id. | All 100 records updated correctly. |
| 4 | Verify 100 individual audit entries were created. | 100 audit log entries exist, one per employee. |
| 5 | Verify Manager M's direct-reports endpoint returns 100 employees. | Direct-reports count = 100. |

## 6. Postconditions
- 100 employees assigned to Manager M within the SLA. 100 audit entries created.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
