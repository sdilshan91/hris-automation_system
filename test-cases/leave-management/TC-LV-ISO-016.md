---
id: TC-LV-ISO-016
user_story: US-LV-004
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-ISO-016: Inline-balance cache keys for the pending queue are tenant-scoped (DEFERRED -- partial)

## 1. Test Objective
Verify that the inline balances shown in the pending queue are isolated per tenant. Since the inline balance is read from the LeaveLedger running total (Redis cache is DEFERRED), this test confirms the DB-fallback path is tenant-scoped now and that the documented cache-key pattern is tenant-prefixed so caching can be added safely later.

## 2. Related Requirements
- User Story: US-LV-004
- Non-Functional Requirements: NFR-2 (Redis-cached balance -- DEFERRED; DB-fallback verified)
- Data Requirements: Section 7 (balance cache key `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}`)

## 3. Preconditions
- Tenants "acme" and "globex" each have employees with computed leave balances (US-LV-002).
- An employee may share an `employeeId`/`leaveTypeId`-shaped value across tenants only by coincidence; isolation must hold regardless.
- No Redis cache layer is built yet; balances derive from the LeaveLedger running total (per vault `modules/leave-management.md`).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Documented key pattern | `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}` | Tenant-prefixed |
| acme balance | Jane Annual = 11.00 | Tenant A |
| globex balance | (separate employees) | Tenant B |
| Cache layer | Not implemented | DEFERRED |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As Robert (acme), load the pending queue and read the inline balances | Balances reflect acme's LeaveLedger running totals only; no globex balance values appear. |
| 2 | As Sara (globex), load the pending queue | Balances reflect globex's ledger only; acme balances are not served. |
| 3 | Verify the balance read is tenant-scoped at the DB layer | The ledger query is filtered by the resolved tenant (EF global query filter); no cross-tenant balance is computed. |
| 4 | Verify the documented cache key is tenant-prefixed | The key pattern begins with `tenant:{tenantId}:` so a future Redis cache cannot collide across tenants. |
| 5 | Mark the cache portion DEFERRED | Because no Redis cache exists yet, the cache-isolation assertion is verified against the documented key pattern and the DB-fallback path -- the live-cache step is DEFERRED pending Redis implementation (consistent with TC-LV-ISO-008/012). |

## 6. Postconditions
- No data mutated.
- Inline balances are tenant-isolated via the DB path; the tenant-scoped cache-key pattern is confirmed for future Redis caching.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
