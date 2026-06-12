---
id: TC-CHR-231
user_story: US-CHR-009
module: Core HR
priority: high
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-231: Employment history -- 3 sequential status changes produce 3 timeline entries with correct data

## 1. Test Objective
Verify that performing multiple sequential status changes on an employee results in the correct number of employment history entries, each with accurate previous/new status, reason, effective date, and actor. The UI timeline displays all entries in reverse chronological order. This validates FR-4 and the test hint for employment history verification.

## 2. Related Requirements
- User Story: US-CHR-009
- Functional Requirements: FR-4
- Acceptance Criteria: AC-2

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user (`hr-officer-uuid`) is authenticated in the "acme" tenant context.
- Employee "Eve Martin" (`emp-007-uuid`) exists with status `active`.
- No prior status change history exists for this employee.

## 4. Test Data
| Change # | From | To | Reason | Effective Date |
|----------|------|----|--------|---------------|
| 1 | active | suspended | Under investigation | 2026-06-12 |
| 2 | suspended | active | Investigation cleared | 2026-06-12 |
| 3 | active | terminated | Resignation | 2026-06-12 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Send status change #1: active -> suspended with reason "Under investigation". | 200 OK. Status = suspended. |
| 2 | Send status change #2: suspended -> active with reason "Investigation cleared". | 200 OK. Status = active. |
| 3 | Send status change #3: active -> terminated with reason "Resignation". | 200 OK. Status = terminated. |
| 4 | Query the employment history table for emp-007-uuid ordered by changed_at. | Exactly 3 records exist: (1) active->suspended, (2) suspended->active, (3) active->terminated. Each has the correct `previous_value`, `new_value`, `reason`, `effective_date`, `changed_by` = hr-officer-uuid, and `tenant_id` = acme. |
| 5 | Navigate to the employee profile in the UI and view the Employment History section. | A vertical timeline displays 3 entries in reverse chronological order (most recent first): (1) "Terminated" red badge with reason "Resignation", (2) "Active" green badge with reason "Investigation cleared", (3) "Suspended" gray badge with reason "Under investigation". Each entry shows the date, status badge, reason, and "Changed by Sarah HR" (or equivalent actor name). |
| 6 | Verify each timeline entry has the correct status badge color. | Terminated = red, Active = green, Suspended = gray, matching the badge color mapping in the user story. |

## 6. Postconditions
- Employee status is `terminated`.
- Employment history contains exactly 3 entries in chronological order.
- UI timeline renders all 3 entries correctly.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
