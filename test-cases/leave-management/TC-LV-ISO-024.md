---
id: TC-LV-ISO-024
user_story: US-LV-006
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-14
---

# TC-LV-ISO-024: Balance cache keys are tenant- and employee-scoped (Redis DEFERRED -- partial)

## 1. Test Objective
Verify that the balance cache key design is tenant- and employee-scoped so that no two tenants (or two employees) can collide on or read each other's cached balances. The Redis balance cache is DEFERRED module-wide; this test verifies the documented key pattern and the DB-fallback isolation, marking the live-cache portion conditional (NFR-3, FR-5, Section 7).

## 2. Related Requirements
- User Story: US-LV-006
- Non-Functional Requirements: NFR-1, NFR-3
- Functional Requirements: FR-5
- Note: Redis balance cache DEFERRED (per docs/vault/modules/leave-management.md); key pattern `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}` (FR-5) verified by design; DB-fallback isolation verified live.

## 3. Preconditions
- Tenant "acme" (Nina) and Tenant "globex" (Lara) each have computed balances. Documented cache key includes both `tenantId` and `employeeId`.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Key pattern | `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}` | FR-5 |
| Same leaveTypeId across tenants | yes | Collision probe |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Verify the documented cache-key pattern | The key embeds `tenantId` AND `employeeId` (and leaveTypeId), so acme/Nina and globex/Lara never share a key even for the same leaveTypeId. |
| 2 | (DEFERRED -- cache present) Populate the cache for Nina, then request Lara's balance | Lara's request resolves under her own tenant+employee key; Nina's cached value is never served to Lara. Mark CONDITIONAL on the Redis cache layer. |
| 3 | DB-fallback (live, cache absent) | With no cache layer, balances are computed per-tenant per-employee from the ledger and remain isolated (cross-check TC-LV-ISO-021/023). |
| 4 | Record deferral honestly | The live cache-key isolation is DEFERRED pending Redis; the tenant+employee key pattern and DB-fallback isolation are verified now (not a silent gap). |

## 6. Postconditions
- Cache-key design is tenant+employee scoped; live verification deferred to the Redis layer; DB-fallback isolation confirmed.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
