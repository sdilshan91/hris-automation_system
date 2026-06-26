---
id: TC-PRF-ISO-033
user_story: US-PRF-009
module: Performance Management
priority: critical
type: security
status: fail
created: 2026-06-16
---

# TC-PRF-ISO-033: Goal progress updates (+ comments, attachments, stale flags) in Tenant A invisible from Tenant B -- cross-tenant READ isolation, incl. by direct id (NFR-2)

## 1. Test Objective
Verify NFR-2: all goal-tracking data -- goal_progress_updates, goal_comments, update attachments, overall-completion aggregates, and "Needs Attention" stale flags -- is isolated per tenant. An employee/manager/HR authenticated in Tenant B can never read, list, or retrieve any Tenant A progress update, comment, or attachment, including by passing a Tenant A goal/update/comment id directly. Exercises the platform tenant-isolation mechanism (EF Core global query filters + TenantInterceptor; goal-tracking tables scoped by tenant_id).

> Note: US-PRF-009 NFR-2 / S7 specify PostgreSQL RLS on goal_progress_updates (and goal_comments). This platform currently enforces isolation via EF Core global query filters + TenantInterceptor. If RLS is later added, extend Step 4 to assert isolation at the DB session level as defense-in-depth (same caveat as US-PRF-001..008).

## 2. Related Requirements
- User Story: US-PRF-009
- Non-Functional Requirements: NFR-2
- Data Requirements: S7 (goal_progress_updates, goal_comments; tenant_id scoped)

## 3. Preconditions
- Tenant "acme" (Tenant A) has goals with progress updates, comments, attachments, and a stale "Needs Attention" flag; known goalId / updateId / commentId.
- Tenant "globex" (Tenant B) has its own goals/updates and a manager + HR Officer.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | has Sam's updates |
| Tenant B | globex | its own updates |
| Auth context | globex | Tenant B |
| acme ids | goalId, updateId, commentId | direct-id probe |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Authenticate in globex; JWT carries globex `tenant_id` | Tenant context resolves to globex. |
| 2 | `GET .../performance/my-goals` and `.../team-goals` as globex | Only globex goals/updates returned; ZERO acme data (NFR-2). |
| 3 | `GET .../performance/goals/{acme_goalId}/updates`, `.../updates/{acme_updateId}`, and the comments endpoint with acme ids | 404 / empty -- the global query filter excludes acme rows; never 200 with acme update/comment/attachment data. |
| 4 | Verify at the DB level | `SELECT * FROM goal_progress_updates WHERE tenant_id = acme_id` returns only acme rows; a session/context set to globex never reads acme update/comment rows. (If RLS exists, confirm a globex-set session cannot read acme rows even via a direct query.) |
| 5 | Switch to acme and repeat | acme sees only its own updates/comments; zero globex data. |

## 6. Postconditions
- No cross-tenant progress-update, comment, attachment, aggregate, or stale-flag data is exposed via API or direct id. No cross-tenant leakage.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
