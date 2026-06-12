---
id: TC-CHR-273
user_story: US-CHR-011
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-273: Circular reporting chain detection -- indirect cycle A->B->C then C->A rejected

## 1. Test Objective
Verify that the system detects and rejects an indirect circular reporting chain of depth 3. Given A reports to B and B reports to C, attempting to set C to report to A is rejected with the exact AC-3 error message. This validates AC-3 and FR-3 for chains deeper than a direct cycle.

## 2. Related Requirements
- User Story: US-CHR-011
- Acceptance Criteria: AC-3
- Functional Requirements: FR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated.
- Employees A, B, and C exist with status `active`.
- Chain: A reports to B (`A.reports_to = B.id`), B reports to C (`B.reports_to = C.id`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Employee A | alice@acme.test | Reports to B |
| Employee B | bob@acme.test | Reports to C, has A as direct report |
| Employee C | charlie@acme.test | Has B as direct report (chain root) |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify the chain: A -> B -> C. Query each employee's `reports_to_employee_id`. | A.reports_to = B.id, B.reports_to = C.id, C.reports_to = null. |
| 2 | Navigate to Employee C's profile and click edit on Reporting Manager. | Manager selector opens. |
| 3 | Search for and select Employee A as the manager for Employee C. | Employee A appears in the search results. |
| 4 | Attempt to save the assignment (C reports to A). | The system rejects with: "Circular reporting chain detected. [Employee C] cannot report to [Employee A] because [Employee A] already reports to [Employee C] (directly or indirectly)." |
| 5 | Verify via API that Employee C's record is unchanged. | `reports_to_employee_id` remains null. |
| 6 | Verify no employment history or audit entry was created for this failed attempt. | No new entries. |

## 6. Postconditions
- No state change. The chain remains A -> B -> C (no cycle).

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
