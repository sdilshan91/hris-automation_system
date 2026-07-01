---
id: TC-RPT-ISO-012
user_story: US-RPT-003
module: Reports & Analytics
priority: high
type: security
status: fail
exec_note: "2026-06-30 API iso-fixture probe: report cache key NOT tenant-isolated — isoa-tok+isob-hdr headcount returned isob's cached result (generatedAt reused from isob own call). Cross-tenant cache collision. BUG-003 class (ISSUE-193)."
created: 2026-06-17
---

# TC-RPT-ISO-012: Payroll report cache keys tenant-prefixed; no cross-tenant cache collision (AC-5, FR-7, NFR-2)

## 1. Test Objective
Verify the Redis report cache (FR-7, TTL 15 min) is tenant-scoped: cache keys are prefixed with the
tenant id so Tenant A and Tenant B requesting an IDENTICAL report (same type + params) never collide,
and a cached Tenant A result is never served to a Tenant B requester. Validates AC-5, FR-7, NFR-2.

> CONDITIONAL / DEFERRED INFRA: Redis is a deferred dev-box infra item (carried from US-RPT-001/002
> and Payroll/Leave/Attendance). If Redis is wired, assert the tenant-prefixed key shape + isolation +
> no-collision + per-tenant invalidation. If not wired, assert the key-derivation logic includes the
> tenant prefix and that identical params across tenants resolve to DISTINCT keys. The NFR-1 5s
> threshold is never relaxed to compensate for an absent cache.

## 2. Related Requirements
- User Story: US-RPT-003
- Acceptance Criteria: AC-5
- Functional Requirements: FR-7 (Redis cache, TTL 15 min), FR-8
- Non-Functional: NFR-2

## 3. Preconditions
- Tenant A and Tenant B active with DISTINCT payroll data and a Redis instance (or the key-derivation code under test).
- `hrA` and `hrB` authenticated in their own tenants.
- Identical report request params used in both tenants (e.g. PayrollSummary, period 2026-03).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| cache key shape | t:{tenantId}:payroll-report:{type}:{paramsHash} | tenant-prefixed (FR-7) |
| TTL | 15 min | FR-7 |
| Tenant A result | gross=250,000.00 | must never be served to B |
| Tenant B result | gross=60,000.00 | distinct |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As `hrA`, generate PayrollSummary 2026-03 (populates cache) | Cache entry written under a key beginning with Tenant A's prefix |
| 2 | As `hrB`, generate the IDENTICAL PayrollSummary 2026-03 | Tenant B gets gross=60,000.00 (B's data) — NOT A's cached 250,000.00 |
| 3 | (Redis wired) Inspect both cache keys | Differ only by tenant prefix; identical params -> DISTINCT keys; no collision |
| 4 | (Redis wired) Invalidate/expire Tenant A's entry (15-min TTL or refresh) | Only A's entry affected; B's cache untouched |
| 5 | (Redis NOT wired) Inspect the key-derivation logic | Tenant prefix is part of the key; identical cross-tenant params yield distinct keys |
| 6 | Attempt to read A's cached payload while authenticated as B | Not served; B's request resolves to B's own key only |

## 6. Postconditions
- Cache keys tenant-prefixed; no cross-tenant collision or cache-served leakage.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
