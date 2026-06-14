---
id: TC-LV-253
user_story: US-LV-012
module: Leave Management
priority: medium
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-253: Real-time computed balances (Redis DEFERRED, DB-fallback) + historical prior-year reports (BR-3, BR-5)

## 1. Test Objective
Verify BR-3 — report balances reflect real-time computed values from the Redis cache OR the DB (the Redis cache layer is DEFERRED module-wide, so verify the DB-computed fallback) — and BR-5: reports for previous leave years are available from retained historical data.

## 2. Related Requirements
- User Story: US-LV-012
- Business Rules: BR-3, BR-5
- Note: Redis caching DEFERRED per docs/vault/modules/leave-management.md (balance read from LeaveLedger running total; documented key `tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId}`).

## 3. Preconditions
- Tenant "acme"; employee with ledger data in the current year and a prior year (e.g. 2025 and 2026).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Current year | 2026 | live balances |
| Prior year | 2025 | BR-5 historical |
| Retention | 7 years | §6 BR-5 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run the Balance Summary, then apply a fresh `used`/`adjusted` ledger entry and re-run | The balance reflects the new value immediately (real-time), computed from the LeaveLedger running total (DB-fallback path; Redis cache step recorded CONDITIONAL/DEFERRED). |
| 2 | (DEFERRED — cache present) Confirm cache read/invalidation | When Redis is wired, the report reads the cached balance and invalidates on ledger writes; mark this step CONDITIONAL on the Redis layer (not a silent pass). |
| 3 | Select the prior leave year (2025) on a report | The report returns 2025 historical figures from retained data (BR-5), distinct from 2026. |
| 4 | Verify retention boundary | Data within the 7-year retention window is queryable; beyond it may be purged per policy (record as out-of-scope/retention-policy dependent). |

## 6. Postconditions
- Balances are real-time (DB-fallback verified, Redis DEFERRED); prior-year reports are available from retained data.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
