---
id: TC-LV-121
user_story: US-LV-006
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-121: Cache miss -- balance computed from the ledger and re-cached (Redis DEFERRED; DB-fallback verified)

## 1. Test Objective
Verify the balance source-of-truth path: on a cache miss the balance is computed from `leave_ledger` and returned correctly, and (when the cache layer exists) the computed value is re-cached under the tenant-scoped key. The Redis cache layer is DEFERRED module-wide; this test verifies the DB-computed fallback and documents the cache portion as conditional (FR-5, NFR-1).

## 2. Related Requirements
- User Story: US-LV-006
- Functional Requirements: FR-5
- Non-Functional Requirements: NFR-1
- Note: Redis balance cache DEFERRED per docs/vault/modules/leave-management.md (no entity uses a cache layer yet; balance is read from the LeaveLedger running total).

## 3. Preconditions
- Tenant "acme" active; employee "Nina Patel" authenticated with known ledger-derived balances.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Documented cache key | `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}` | Per FR-5 / vault |
| Annual ledger balance | 11 | Computed from ledger |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Ensure no cached balance exists for the employee (cache empty / not implemented) | The system has no cached value to read. |
| 2 | Call `GET /api/v1/leaves/my-balance` | 200 with `balance` computed directly from the `leave_ledger` running total (e.g., Annual=11); values match the BR-1 formula. |
| 3 | (DEFERRED -- cache present) After the call, inspect the cache | The computed value is written under `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}`. Mark this step CONDITIONAL on the Redis cache layer; when not implemented, record as DEFERRED (not a failure). |
| 4 | (DEFERRED) Clear the cache and re-request | Balance recomputes from the ledger and (if cache present) is re-cached -- value is stable across cache miss/hit. |

## 6. Postconditions
- DB-fallback balance computation is correct and stable; cache re-population verified only when the Redis layer exists (otherwise DEFERRED).

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
