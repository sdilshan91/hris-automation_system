---
id: TC-LV-ISO-032
user_story: US-LV-008
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-032: Carry-forward balance cache keys are tenant- and employee-scoped (NFR-2; Redis DEFERRED -- partial)

## 1. Test Objective
Verify the cache-key design for carry-forward-affected balances is tenant- and employee-scoped so two tenants (or two employees) can never collide on or read each other's cached balance after the year-end/expiry jobs invalidate it. The Redis cache (FR-7) is DEFERRED module-wide; the tenant-scoped key pattern is verified by design and the DB-fallback path is verified live, with the live-cache portion recorded as conditional (NFR-2, FR-7).

## 2. Related Requirements
- User Story: US-LV-008
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-7 (cache invalidation -- DEFERRED)
- Note: Redis balance cache DEFERRED per docs/vault/modules/leave-management.md (balance read from LeaveLedger running total). Documented key pattern: `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}`.

## 3. Preconditions
- Tenant "acme" (Sam) and Tenant "globex" (Dana), each with carry-forward and new-entitlement balances for the same leave type/year.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Key pattern | `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}` | tenant + employee scoped |
| Same type/year both tenants | Annual / 2027 | collision probe |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify the documented/proposed cache-key pattern | The key embeds both `tenantId` and `employeeId`, so acme/Sam and globex/Dana can never share a key even for the same leave type/year. |
| 2 | (DEFERRED -- cache present) After the year-end job invalidates Sam's balance, read Dana's balance | Dana resolves under her own tenant+employee-scoped key; Sam's invalidated/cached value is never served to Dana. Mark CONDITIONAL on the Redis layer. |
| 3 | DB-fallback (live, cache absent) | With no cache layer, balances (including carry-forward and expired) are recomputed per-tenant from the ledger via the EF global query filter and remain isolated (cross-ref TC-LV-ISO-031). |
| 4 | Record deferral honestly | Live cache-key isolation and post-job invalidation are DEFERRED pending Redis; the tenant+employee-scoped key pattern and DB-fallback isolation are verified now (not a silent gap). |

## 6. Postconditions
- Balance cache-key design is tenant- and employee-scoped; live cache verification deferred to Redis; DB-fallback isolation confirmed.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Security test
- [ ] Boundary test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
