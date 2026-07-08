---
id: TC-PRF-004-B2
user_story: US-PRF-004
module: Performance Management
priority: high
type: security
status: draft
created: 2026-07-08
---

# TC-PRF-004-B2: Current-cycle resolver — low-privilege "resolve my active cycle" for employee (Read.Self) and manager (Review.Team); today cycles/active is HR-gated (BUG-243 follow-up)

**Status:** DRAFT — BLOCKED: endpoint not yet implemented (BUG-243 follow-up)

> `GET cycles/active` EXISTS but is HR-gated (`Performance.SetGoal.All` / `Performance.Publish.All`), so an employee (`Performance.Read.Self`) or a manager (`Performance.Review.Team`) **cannot resolve their own active cycle id** — which forces the FE to key self-assessment / manager-review / sign-off / feedback-360 flows off reviewId/assignmentId and is the root enabler behind BUG-243/BUG-244. This stub documents the intended low-privilege resolver. Highest-leverage single item — unblocks the manager-review/sign-off half.

## 1. Test Objective
Verify a to-be-built low-privilege resolver (e.g. `GET .../performance/cycles/current`) that returns the active/current appraisal cycle (at minimum the cycle id, name, and window state) for the **calling** employee/manager, gated by a broad read permission that ordinary employees and managers hold — NOT the HR-only cycle-admin permission. Tenant-scoped: the resolver returns the caller's own tenant's active cycle only.

## 2. Related Requirements
- User Story: US-PRF-004
- Acceptance Criteria: AC-B2 (low-privilege current/active-cycle resolver)
- Functional Requirements: enables US-PRF-002 (self-assessment), US-PRF-003 (manager review), US-PRF-005 (360), US-PRF-006 (sign-off) to resolve the cycle id without HR privilege
- Defect: BUG-243 (parent; missing-endpoint / HR-gated-resolver half = BUG-244)

## 3. Preconditions
- Endpoint implemented (removes this BLOCK).
- Tenant "acme" Active with exactly one active cycle "FY26-H1" (window open).
- Three acme personas authenticated: an employee (`Performance.Read.Self`), a manager (`Performance.Review.Team`), and an HR Officer (cycle-admin).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Active cycle | FY26-H1 | window open |
| Employee | Asha (`Performance.Read.Self`) | must resolve cycle id |
| Manager | Ravi (`Performance.Review.Team`) | must resolve cycle id |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As the employee (`Performance.Read.Self`), `GET .../performance/cycles/current` | 200; returns FY26-H1 cycle id + name + window state. (Contrast: today `GET cycles/active` returns 403 for this persona.) |
| 2 | As the manager (`Performance.Review.Team`), GET the resolver | 200; returns FY26-H1 cycle id. |
| 3 | As HR (cycle-admin), GET the resolver | 200 (superset of read permission). |
| 4 | Regression contrast on the existing admin route | `GET cycles/active` still requires HR admin permission (employee/manager → 403); the new low-privilege resolver does NOT widen the admin route. |
| 5 | With no active cycle (all cycles closed/future) | Coded 404 / empty "no active cycle" — not a 500. |
| 6 | Confirm tenant scoping | The resolver returns only the caller's tenant's active cycle; an other-tenant caller resolves their own, never acme's (ISO suite). |

## 6. Postconditions
- Read-only; employees and managers can resolve their active cycle id with a low-privilege permission, unblocking the FE flows that currently depend on reviewId/assignmentId; the HR-only admin cycle route is unchanged.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
