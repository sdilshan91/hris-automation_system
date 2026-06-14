---
id: TC-ATT-ISO-004
user_story: US-ATT-001
module: Attendance
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-ATT-ISO-004: Attendance dashboard cache keys are tenant-scoped (no cross-tenant cache bleed)

## 1. Test Objective
Verify FR-6 cache isolation: the clock-in status cache key includes the `tenant_id` (`att:{tenant_id}:{employee_id}:{date}`) so two tenants that happen to share the same employee UUID space or date can never read each other's cached status. A clock-in in Tenant A must not surface as a clocked-in status in Tenant B's dashboard read.

## 2. Related Requirements
- User Story: US-ATT-001
- Functional Requirements: FR-6
- Non-Functional Requirements: NFR-2

## 3. Preconditions
- Tenant "acme" and Tenant "globex" both exist and are `active`, Attendance module enabled.
- Employee "Jordan Lee" (acme) and employee "Sam Doe" (globex) exist; both authenticated as appropriate per step.
- The Redis cache layer is available. (If the cache layer is not yet wired in this build, mark this TC as conditional and verify the equivalent tenant-scoped read from the DB; record which path was exercised.)

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Cache key pattern | att:{tenant_id}:{employee_id}:{date} | FR-6, tenant-scoped |
| Tenant A | acme | Jordan Lee clocks in |
| Tenant B | globex | Sam Doe does not clock in |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Jordan Lee (acme), clock in successfully | Cache key `att:{acme_id}:{jordan_id}:{date}` is set to clocked-in. |
| 2 | Inspect the cache | The key is prefixed/scoped with acme's `tenant_id`. No key exists under globex's `tenant_id` for this action. |
| 3 | As Sam Doe (globex), load the dashboard / today-status | globex shows Sam Doe as NOT clocked in. The acme clock-in does not leak into globex's status. |
| 4 | Construct a key collision test: if acme and globex employee UUIDs were ever equal, verify the tenant prefix still separates them | The tenant_id segment guarantees distinct keys; no read returns the other tenant's value. |
| 5 | Verify cache invalidation/refresh stays tenant-scoped | Clock-out or expiry in one tenant does not alter the other tenant's cached status. |

## 6. Postconditions
- Cache reads/writes for attendance status are isolated by tenant; no cross-tenant cache bleed.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
