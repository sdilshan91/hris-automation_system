---
id: TC-CHR-290
user_story: US-CHR-011
module: Core HR
priority: critical
type: performance
status: draft
created: 2026-06-12
---

# TC-CHR-290: Deep hierarchy (10 levels) cycle detection completes within 200ms

## 1. Test Objective
Verify that the cycle detection algorithm completes within 200 ms even for deeply nested hierarchies (10+ levels). A 10-level chain should be traversed efficiently without timeouts or performance degradation. This validates NFR-2.

## 2. Related Requirements
- User Story: US-CHR-011
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-3

## 3. Preconditions
- Tenant "acme" exists with status `active`.
- An HR Officer user is authenticated.
- A 10-level deep reporting chain exists: E1 -> E2 -> E3 -> ... -> E10 (E1 reports to E2, E2 to E3, etc.).
- Employee X exists outside the chain.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Chain | E1 -> E2 -> ... -> E10 | 10-level deep chain |
| Employee X | x@acme.test | Outside chain; to be assigned as E10's manager |
| Cycle attempt | E10's manager set to E1 | Would create a 10-level cycle |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Create or verify the 10-level chain: E1.reports_to = E2, E2.reports_to = E3, ..., E9.reports_to = E10, E10.reports_to = null. | Chain confirmed via API queries. |
| 2 | Attempt to set E10.reports_to = E1 (creating a cycle). Record the API response time. | The system detects the cycle and rejects within 200 ms. Error message references the circular chain. |
| 3 | Repeat step 2 five times and record each response time. | All 5 attempts are rejected. All response times are <= 200 ms. |
| 4 | Assign Employee X (outside the chain) as E10's manager. Record the response time. | Assignment succeeds (no cycle). Response time is within 200 ms for the cycle check portion. |

## 6. Postconditions
- E10 has Employee X as manager. Cycle detection was verified within time constraints.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
