---
id: TC-LV-ISO-044
user_story: US-LV-011
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-044: LOP/balance cache keys are tenant- and employee-scoped (Redis DEFERRED — partial) (NFR-2)

## 1. Test Objective
Verify that any cache key used for LOP-affected balances embeds the tenant id and the employee id, so an LOP assignment for a Tenant A employee can never read or invalidate a Tenant B (or another employee's) cached balance. The Redis cache layer is DEFERRED module-wide; the key design is verified by design and the DB-fallback isolation is verified live, with the live-cache portion recorded as conditional (NFR-2).

## 2. Related Requirements
- User Story: US-LV-011
- Non-Functional Requirements: NFR-2
- Note: Redis caching DEFERRED per docs/vault/modules/leave-management.md (no entity uses a cache layer; balance read from the LeaveLedger running total). Documented key pattern: `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}`.

## 3. Preconditions
- Tenant "acme" (Mark) and Tenant "globex" (Kofi), each with leave balances; LOP assignment available in acme.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Key pattern | `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}` | documented |
| Collision probe | same leave-type, both tenants/employees | must not collide |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify the documented/proposed cache-key pattern | The key embeds `tenantId` and `employeeId`, so acme/Mark and globex/Kofi — and two employees within one tenant — never share a key for the same leave type. |
| 2 | (DEFERRED — cache present) Assign LOP / convert an LOP entry for Mark (acme) and observe invalidation | Only `tenant:acme:leave_balance:{markId}:{...}` is invalidated; globex's and other acme employees' cached balances are untouched. Mark this step CONDITIONAL on the Redis layer. |
| 3 | DB-fallback (live, cache absent) | With no cache layer, the LOP/override balance effect (e.g. a convert-to-Casual `used` entry) updates only Mark's LeaveLedger running total under acme's tenant filter; globex/Kofi's balance is unchanged (cross-ref TC-LV-ISO-043). |
| 4 | Confirm no cross-employee/tenant effect | An LOP operation on Mark does not alter any other employee's or tenant's cached/derived balance. |

## 6. Postconditions
- LOP/balance cache-key design is tenant- and employee-scoped; live cache-invalidation deferred to Redis; DB-fallback isolation confirmed (not a silent gap).

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
