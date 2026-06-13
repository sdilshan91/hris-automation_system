---
id: TC-LV-ISO-013
user_story: US-LV-004
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-ISO-013: Manager in Tenant A cannot see Tenant B's pending leave requests

## 1. Test Objective
Verify that the pending leave queue is fully tenant-isolated: a manager authenticated in Tenant A sees only Tenant A's pending requests for their team, and never any pending requests, employees, or balances belonging to Tenant B. (Test Hint: Manager in Tenant A must not see requests from Tenant B.)

## 2. Related Requirements
- User Story: US-LV-004
- Non-Functional Requirements: NFR-3
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with manager "Robert Lee" (`Leave.Approve.Team`) and direct reports with pending requests.
- Tenant "globex" exists with manager "Sara Kim" (`Leave.Approve.Team`) and direct reports with pending requests.
- Robert is authenticated in the "acme" tenant context.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | Robert Lee, team pending requests |
| Tenant B | globex | Sara Kim, team pending requests |
| acme pending | 3 | Visible to Robert |
| globex pending | 4 | Must never be visible to Robert |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Robert (acme), call `GET /api/v1/leaves/pending` | Only acme's pending requests for Robert's team are returned; zero globex requests. |
| 2 | As Robert (acme), attempt `GET /api/v1/leaves/pending/{globex_request_id}` using a known globex request UUID | Response 404 Not Found (filtered by tenant query filter). |
| 3 | As Robert (acme), filter by a globex employee id | Empty result -- the globex employee is not visible in acme's context. |
| 4 | Switch to globex (Sara) and call the queue | Only globex pending requests appear; no acme data leaks. |
| 5 | Verify balances and employee names in the response are tenant-local | All inline balances and employee identities belong to acme only. |

## 6. Postconditions
- No data mutated.
- Cross-tenant reads of pending requests return no data in either direction.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
