---
id: TC-LV-038
user_story: US-LV-002
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-13
---

# TC-LV-038: Entitlement rule CRUD validation -- invalid inputs rejected

## 1. Test Objective
Verify that the entitlement rule API rejects invalid inputs with appropriate validation error messages, including missing required fields, invalid references, and constraint violations.

## 2. Related Requirements
- User Story: US-LV-002
- Functional Requirements: FR-1
- Business Rules: BR-4

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- A user with `Leave.Configure` permission is authenticated in the "acme" tenant context.
- Leave type "Annual Leave" exists and is active.

## 4. Test Data
| Scenario | Input | Expected Error |
|----------|-------|----------------|
| Missing leave_type_id | leave_type_id = null | "Leave type is required." |
| Non-existent leave_type_id | leave_type_id = random UUID | "Leave type not found." |
| Non-existent department_id | department_id = random UUID | "Department not found." |
| Non-existent job_level_id | job_level_id = random UUID | "Job level not found." |
| Missing entitlement_days | entitlement_days = null | "Entitlement days is required." |
| Negative entitlement_days | entitlement_days = -5 | "Entitlement days must be zero or greater." |
| Exceeds max entitlement | entitlement_days = 999.99 | "Entitlement days cannot exceed 365." |
| Missing effective_from | effective_from = null | "Effective from date is required." |
| effective_to before effective_from | effective_from = 2027-01-01, effective_to = 2026-01-01 | "Effective to must be after effective from." |
| Duplicate rule | Same leave_type + dept + level combo | "An entitlement rule with these criteria already exists." |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send `POST /api/v1/leave-entitlement-rules` with `leave_type_id = null` | 400 Bad Request: "Leave type is required." |
| 2 | Send `POST /api/v1/leave-entitlement-rules` with non-existent `leave_type_id` | 400 Bad Request: "Leave type not found." |
| 3 | Send `POST /api/v1/leave-entitlement-rules` with non-existent `department_id` | 400 Bad Request: "Department not found." |
| 4 | Send `POST /api/v1/leave-entitlement-rules` with non-existent `job_level_id` | 400 Bad Request: "Job level not found." |
| 5 | Send `POST /api/v1/leave-entitlement-rules` with `entitlement_days = null` | 400 Bad Request: "Entitlement days is required." |
| 6 | Send `POST /api/v1/leave-entitlement-rules` with `entitlement_days = -5.00` | 400 Bad Request: "Entitlement days must be zero or greater." |
| 7 | Send `POST /api/v1/leave-entitlement-rules` with `entitlement_days = 999.99` | 400 Bad Request: "Entitlement days cannot exceed 365." (or appropriate max). |
| 8 | Send `POST /api/v1/leave-entitlement-rules` with `effective_from = null` | 400 Bad Request: "Effective from date is required." |
| 9 | Send `POST /api/v1/leave-entitlement-rules` with `effective_to = 2025-01-01, effective_from = 2026-01-01` | 400 Bad Request: "Effective to must be after effective from." |
| 10 | Create a valid rule for Annual Leave + Engineering + Senior = 25 days | 201 Created. |
| 11 | Attempt to create an identical rule (same leave_type + dept + level combination) | 409 Conflict or 400 Bad Request: "An entitlement rule with these criteria already exists." |
| 12 | Attempt `PUT /api/v1/leave-entitlement-rules/{id}` with `entitlement_days = -1` | 400 Bad Request: validation error. |
| 13 | Attempt `DELETE /api/v1/leave-entitlement-rules/{non_existent_id}` | 404 Not Found. |

## 6. Postconditions
- No invalid records created in `leave_entitlement_rule` table.
- All error messages are user-friendly and specific.
- Valid data passes all validation checks.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
