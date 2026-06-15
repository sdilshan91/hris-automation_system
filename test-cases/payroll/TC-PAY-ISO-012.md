---
id: TC-PAY-ISO-012
user_story: US-PAY-003
module: Payroll
priority: high
type: security
status: draft
created: 2026-06-15
---

# TC-PAY-ISO-012: SignalR progress group, run notifications, distributed lock, and structure cache are all tenant-scoped (no cross-tenant leak)

## 1. Test Objective
Verify AC-7 / FR-6 / NFR-3 / NFR-7: the real-time and cross-cutting infrastructure of a payroll run is tenant-scoped. The SignalR progress group/event for a run is scoped to the owning tenant (Tenant B never receives Tenant A's "Processing X/Y" updates or completion notification); the distributed lock key that prevents concurrent runs (NFR-3) is keyed by tenant+period (so Tenant A's lock for May 2026 does NOT block Tenant B's May 2026 run); and the salary-structure cache read during processing (NFR-7) uses tenant-scoped keys (no cross-tenant cache hit). (If structure cache or progress is computed on-demand without Redis today, the cache/lock-key steps are CONDITIONAL and assert no shared/global key is used.)

## 2. Related Requirements
- User Story: US-PAY-003
- Acceptance Criteria: AC-7
- Functional Requirements: FR-6 (SignalR progress), FR-8
- Non-Functional: NFR-3 (distributed lock per tenant+period), NFR-7 (Redis-cached structure reads)
- Data Requirements: S7 (tenant_id discriminator)

## 3. Preconditions
- Tenant "acme" (A) and "globex" (B) both have a May 2026 run that can be initiated.
- SignalR + distributed lock + structure cache infrastructure available (or note CONDITIONAL).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Lock key | `tenant:{tenantId}:payroll:{year}:{month}` | per tenant+period (NFR-3) |
| Cache key | `tenant:{tenantId}:payroll:structures` | tenant-scoped (NFR-7) |
| SignalR group | per-tenant run group | FR-6 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | acme initiates a run; a globex user is connected to SignalR. | globex receives NO progress/completion events for acme's run; only acme's own clients in the acme run group receive "Processing X/Y" + ReviewPending notification. |
| 2 | Inspect the SignalR group/event scoping. | The progress group id incorporates the tenant (and run) id; subscribing from another tenant yields no events (cross-ref TC-PAY-ISO-010 step 4). |
| 3 | Hold acme's distributed lock for May 2026; initiate globex's May 2026 run concurrently. | globex's run proceeds -- the lock is keyed by tenant+period, so acme's May lock does not block globex's May run (NFR-3 isolation). |
| 4 | Within ONE tenant, attempt a second concurrent May run. | Blocked by the same-tenant lock (only one run per tenant+period) -- confirms the lock still works intra-tenant. |
| 5 | Inspect structure cache reads during acme's run. | Cache keys are tenant-scoped (`tenant:acme:...`); a globex structure is never served to acme's compute; no global/shared key. (CONDITIONAL if no cache today -- assert on-demand reads go through the tenant query filter.) |
| 6 | Verify completion notification delivery. | Only acme HR receives acme's run completion in-app/email; globex HR receives nothing about acme's run. |

## 6. Postconditions
- Progress events, notifications, locks, and caches for a payroll run are tenant-scoped; no cross-tenant leak or interference.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
