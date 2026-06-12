---
id: TC-CHR-223
user_story: US-CHR-009
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-223: State machine boundary -- verify all allowed transitions succeed and terminated is terminal (FR-2)

## 1. Test Objective
Exhaustively verify each allowed transition in the status state machine succeeds via API, and confirm that "terminated" is a terminal state with no outbound transitions. This validates FR-2 and BR-3 across all edges of the state machine.

## 2. Related Requirements
- User Story: US-CHR-009
- Functional Requirements: FR-1, FR-2
- Business Rules: BR-1, BR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated in the "acme" tenant context.
- Multiple test employees exist, one per starting status needed. Where a status is reached only via transition (e.g., suspended), set it up by first performing a valid transition.

## 4. Test Data
| Transition | From | To | Valid? |
|-----------|------|----|--------|
| T1 | probation | active | Yes |
| T2 | probation | terminated | Yes |
| T3 | active | suspended | Yes |
| T4 | active | terminated | Yes |
| T5 | active | inactive | Yes |
| T6 | suspended | active | Yes |
| T7 | suspended | terminated | Yes |
| T8 | inactive | active | Yes |
| T9 | inactive | terminated | Yes |
| T10 | terminated | active | No |
| T11 | terminated | probation | No |
| T12 | terminated | suspended | No |
| T13 | terminated | inactive | No |
| T14 | probation | suspended | No |
| T15 | probation | inactive | No |
| T16 | active | probation | No |
| T17 | suspended | probation | No |
| T18 | suspended | inactive | No |
| T19 | inactive | probation | No |
| T20 | inactive | suspended | No |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | For each valid transition (T1-T9): Send `POST /api/v1/tenant/employees/{empId}/status` with appropriate `newStatus`, `reason`, and `effectiveDate`. | Each returns 200 OK. Employee status is updated. Employment history entry is created. |
| 2 | For T1 (probation -> active): Verify status changes from probation to active. | 200 OK. Employee status = active. History entry records previous=probation, new=active. |
| 3 | For T2 (probation -> terminated): Verify status changes and terminal side effects fire. | 200 OK. Employee status = terminated. Portal access disabled. |
| 4 | For T3 (active -> suspended): Verify status changes and suspension side effects fire. | 200 OK. Employee status = suspended. Portal access disabled. |
| 5 | For T4 (active -> terminated): Verify termination side effects. | 200 OK. Employee status = terminated. |
| 6 | For T5 (active -> inactive): Verify status changes. | 200 OK. Employee status = inactive. |
| 7 | For T6 (suspended -> active): Verify portal access re-enabled. | 200 OK. Employee status = active. Portal access re-enabled. |
| 8 | For T7 (suspended -> terminated): Verify termination from suspended state. | 200 OK. Employee status = terminated. |
| 9 | For T8 (inactive -> active): Verify reactivation. | 200 OK. Employee status = active. |
| 10 | For T9 (inactive -> terminated): Verify termination from inactive state. | 200 OK. Employee status = terminated. |
| 11 | For each invalid transition (T10-T20): Send `POST` with the invalid newStatus. | Each returns 400 Bad Request with an appropriate error message describing the invalid transition. No status changes recorded. |
| 12 | Specifically verify T10-T13 (all transitions from terminated): | All return 400. Messages confirm terminated is a terminal state. |

## 6. Postconditions
- All 9 valid transitions produced employment history entries.
- All 11 invalid transitions were rejected with no side effects.
- Terminated employees remain terminated with no outbound transitions.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
