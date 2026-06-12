---
id: TC-CHR-272
user_story: US-CHR-011
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-272: Circular reporting chain detection -- direct cycle A reports to B, B reports to A

## 1. Test Objective
Verify that the system detects and rejects a direct circular reporting chain. When Employee A already reports to Employee B, attempting to set Employee B to report to Employee A is rejected with the exact error message specified in AC-3. This validates AC-3 and FR-3.

## 2. Related Requirements
- User Story: US-CHR-011
- Acceptance Criteria: AC-3
- Functional Requirements: FR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated.
- Employee A and Employee B both exist with status `active`.
- Employee A has `reports_to_employee_id` = B.id (A reports to B).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee A | alice@acme.test | Currently reports to B |
| Employee B | bob@acme.test | Currently has A as direct report |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to Employee B's profile, Employment Details section. | The "Reporting Manager" field shows the current state (no manager, or some other manager). |
| 2 | Click edit on the Reporting Manager field. | Manager selector opens. |
| 3 | Search for and select Employee A as the manager for Employee B. | Employee A appears in the search results (A is active). |
| 4 | Attempt to save the assignment (B reports to A). | The system rejects the save with error message: "Circular reporting chain detected. [Employee B] cannot report to [Employee A] because [Employee A] already reports to [Employee B] (directly or indirectly)." |
| 5 | Verify Employee B's record is unchanged: `GET /api/v1/tenant/employees/{B.id}`. | `reports_to_employee_id` is NOT set to A.id; it retains its previous value. |
| 6 | Verify no employment history entry was created for this failed attempt. | No new entry exists. |

## 6. Postconditions
- No state change. Employee B's manager assignment is unchanged.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
