---
id: TC-PRF-ISO-034
user_story: US-PRF-009
module: Performance Management
priority: critical
type: security
status: pass
created: 2026-06-16
---

# TC-PRF-ISO-034: Goal-tracking APIs reject missing/invalid/mismatched tenant context + cross-tenant IDOR (view/add-update/comment/drill-down) (NFR-2)

## 1. Test Objective
Verify NFR-2: the goal progress-tracking APIs (my-goals, add-update, update history, comments, team-goals drill-down) reject any request lacking a resolvable tenant context, carrying an invalid/unknown tenant, or whose JWT tenant mismatches the resolved subdomain; and they block cross-tenant IDOR -- a user authenticated in Tenant B cannot view/add/comment on a Tenant A goal or update by supplying its id.

## 2. Related Requirements
- User Story: US-PRF-009
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-1, FR-3, FR-5, FR-8

## 3. Preconditions
- Tenant "acme" (Tenant A) has a known goalId/updateId/commentId.
- Tenant "globex" (Tenant B) has an authenticated employee/manager.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme ids | goalId, updateId, commentId | IDOR target |
| globex auth | valid Tenant B token | acting context |
| Bad contexts | none / unknown sub / mismatched | rejection probes |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `GET .../performance/my-goals` with NO tenant subdomain/context | Rejected -- no tenant resolved; 400/401, no data. |
| 2 | Call with an unknown/invalid subdomain (`nope.yourhrm.com`) | Rejected -- tenant not resolved; no cross-tenant fallback. |
| 3 | Authenticate in globex but send `X-Tenant-Subdomain: acme` (JWT tenant != resolved tenant) | Rejected -- the mismatch is detected; the request does not execute against acme. |
| 4 | As globex, `GET .../performance/goals/{acme_goalId}/updates` (IDOR read) | 404 / 403 -- never 200 with acme update data. |
| 5 | As globex, `POST .../performance/goals/{acme_goalId}/updates` (IDOR write) | Rejected -- no acme update is created; the foreign goalId is not found in globex scope. |
| 6 | As globex, attempt to comment on `{acme_updateId}` (IDOR) | Rejected -- no acme comment created. |
| 7 | As a globex manager, attempt to drill into an acme employee's goals by id | Rejected -- team-goals scope is globex + own-reports only. |

## 6. Postconditions
- Every goal-tracking endpoint rejects missing/invalid/mismatched tenant context and blocks cross-tenant IDOR read/write. No acme data exposed or mutated from globex.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
