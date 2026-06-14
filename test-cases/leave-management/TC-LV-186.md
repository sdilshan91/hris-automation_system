---
id: TC-LV-186
user_story: US-LV-009
module: Leave Management
priority: high
type: performance
status: draft
created: 2026-06-14
---

# TC-LV-186: Team-calendar API for a month range responds within 300ms P95 (NFR-1)

## 1. Test Objective
Verify the `GET /api/v1/leaves/team-calendar` endpoint for a one-month range meets the 300ms P95 latency target, leveraging the `leave_request(tenant_id, employee_id, status, start_date)` index. The Redis cache (consistent with the module-wide caching decision) is NOT implemented; this measures the DB-backed path against the target and records the cached path as conditional.

## 2. Related Requirements
- User Story: US-LV-009
- Non-Functional Requirements: NFR-1
- Data Requirements: Section 7 (index leave_request(tenant_id, employee_id, status, start_date))
- Note: Redis caching DEFERRED module-wide per docs/vault/modules/leave-management.md; DB-backed latency measured here.

## 3. Preconditions
- Tenant "acme" seeded with a representative team (e.g. 30-50 direct reports) and a realistic month of leave entries.
- Load tool configured for sustained concurrency against the team-calendar endpoint.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Range | one month (from/to) | NFR-1 |
| Target | P95 <= 300ms | DB-backed path |
| Index | ix on (tenant_id, employee_id, status, start_date) | Section 7 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run a sustained load of month-range requests as a manager | P95 latency <= 300ms over the run; no error responses. |
| 2 | Inspect the query plan | The query uses the `(tenant_id, employee_id, status, start_date)` index (no full table scan). |
| 3 | Run the same against the employee (department) view | Department-scoped query also meets <= 300ms P95. |
| 4 | Record the cache deferral | The Redis-cached read path (NFR-1 cached target) is DEFERRED; the DB-backed path is measured against 300ms and recorded as the verified baseline (not a silent gap). |

## 6. Postconditions
- Month-range calendar API meets 300ms P95 on the DB-backed path; cached path recorded as conditional/deferred.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [x] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
