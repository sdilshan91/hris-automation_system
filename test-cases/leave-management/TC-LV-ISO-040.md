---
id: TC-LV-ISO-040
user_story: US-LV-010
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-040: Balance cache keys invalidated on cancellation are tenant- and employee-scoped (Redis DEFERRED -- partial) (NFR-2, FR-4)

## 1. Test Objective
Verify that any balance cache key invalidated when a cancellation restores balance embeds the tenant id and the employee id, so cancelling Tenant A employee's leave can never invalidate or read Tenant B's (or another employee's) cached balance. The Redis cache layer is DEFERRED module-wide; the key design is verified by design and the DB-fallback isolation is verified live, with the live-cache portion recorded as conditional (NFR-2, FR-4).

## 2. Related Requirements
- User Story: US-LV-010
- Functional Requirements: FR-4
- Non-Functional Requirements: NFR-2
- Note: Redis caching DEFERRED per docs/vault/modules/leave-management.md (no entity uses a cache layer; balance read from LeaveLedger running total). Documented key pattern: `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}`.

## 3. Preconditions
- Tenant "acme" (Jane) and Tenant "globex" (Kofi), each with an APPROVED future request and a cached/derived balance for the same leave-type name.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Key pattern | `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}` | documented |
| Collision probe | same leave-type, both tenants/employees | must not collide |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify the documented/proposed cache-key pattern | The key embeds `tenantId` and `employeeId`, so acme/Jane and globex/Kofi -- and two employees within one tenant -- never share a key for the same leave type. |
| 2 | (DEFERRED -- cache present) Cancel Jane's leave (acme) and observe invalidation | Only `tenant:acme:leave_balance:{janeId}:{...}` is invalidated; globex's and other acme employees' cached balances are untouched. Mark CONDITIONAL on the Redis layer. |
| 3 | DB-fallback (live, cache absent) | With no cache layer, the reversal `adjusted` entry updates only Jane's LeaveLedger running total under acme's tenant filter; globex/Kofi's balance is unchanged (cross-ref TC-LV-ISO-039). |
| 4 | Confirm no cross-employee/tenant restoration | Restoring Jane's balance does not alter any other employee's or tenant's balance. |

## 6. Postconditions
- Balance cache-key design is tenant- and employee-scoped; live cache-invalidation deferred to Redis; DB-fallback isolation confirmed (not a silent gap).

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
