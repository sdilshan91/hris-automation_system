---
id: TC-LV-ISO-020
user_story: US-LV-005
module: Leave Management
priority: critical
type: security
status: draft
created: 2026-06-13
---

# TC-LV-ISO-020: Balance-cache keys invalidated on approval are tenant-scoped (DEFERRED -- partial)

## 1. Test Objective
Verify that the cache key invalidated on approval is tenant-scoped to the documented pattern `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}`, so that approving a request in Tenant A can never invalidate, read, or expose Tenant B's cached balance. NOTE: the Redis balance cache is DEFERRED module-wide (per vault); this TC verifies the tenant-scoped key pattern and the DB-fallback path now, and marks the live Redis invalidation as deferred.

## 2. Related Requirements
- User Story: US-LV-005
- Non-Functional Requirements: NFR-2, NFR-3
- Functional Requirements: FR-3 (cache invalidation on approval)

## 3. Preconditions
- Tenant "acme" and Tenant "globex" each have employees with computed balances.
- Per `docs/vault/modules/leave-management.md`, no entity uses a Redis cache layer yet; balance is read from the LeaveLedger running total.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| acme key | tenant:acme:leave_balance:{empId}:{ltId} | Tenant-prefixed |
| globex key | tenant:globex:leave_balance:{empId}:{ltId} | Tenant-prefixed |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Inspect the cache-invalidation contract for approval (FR-3) | The key includes the `tenant:{tenantId}:` prefix; an acme approval targets only acme-prefixed keys. |
| 2 | Approve an acme request and verify the DB running total | The acme employee's LeaveLedger running total now reflects the deduction (DB-fallback path; no cross-tenant effect). |
| 3 | Confirm no globex key is touched | The globex-prefixed balance key/value is unaffected by the acme approval. |
| 4 | NOTE the deferral | Live Redis invalidation is DEFERRED until the module-wide cache layer is built; this TC verifies the tenant-scoped key pattern and that balances resolve correctly from the DB running total in the meantime. This is recorded as a partial/deferred dependency, NOT a silent pass. |

## 6. Postconditions
- Approval invalidation is tenant-scoped by key prefix; DB-fallback balance is correct; Redis invalidation deferred.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
