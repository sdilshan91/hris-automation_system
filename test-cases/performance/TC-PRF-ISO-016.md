---
id: TC-PRF-ISO-016
user_story: US-PRF-004
module: Performance Management
priority: high
type: security
status: draft
created: 2026-06-16
---

# TC-PRF-ISO-016: Hangfire cycle jobs + dashboard caches + phase/cancellation notifications are tenant-scoped (NFR-2 / NFR-3)

## 1. Test Objective
Verify NFR-2 and NFR-3: the Hangfire jobs scheduled for a cycle (phase-start, deadline-reminder, phase-close, overdue-escalation), any dashboard/aggregate cache, and all cycle notifications (phase transitions, deadline reminders, cancellation) are scoped to the owning tenant — a job/cache/notification for Tenant A never enumerates, reads, or notifies Tenant B's data.

## 2. Related Requirements
- User Story: US-PRF-004
- Non-Functional Requirements: NFR-2, NFR-3
- Functional Requirements: FR-5
- Business Rules: BR-6 (cancellation notifies all participants)

## 3. Preconditions
- Tenant "acme" and "globex" each have an active cycle with scheduled Hangfire jobs and participants.
- A dashboard/aggregate cache layer may or may not exist (assertion is conditional below).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | cycle + jobs + participants |
| Tenant B | globex | cycle + jobs + participants |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Inspect the acme cycle's Hangfire jobs | Each job carries the acme tenant context in its arguments and, on execution, resolves tenant=acme; it enumerates only acme participants (NFR-3). |
| 2 | Trigger the acme deadline-reminder job | Only acme non-completers are notified; zero globex participants are touched (NFR-2). |
| 3 | Cancel the acme cycle (BR-6) and observe notifications | Only acme participants of that cycle receive the cancellation notice; globex participants receive nothing. |
| 4 | Inspect dashboard/aggregate cache keys (CONDITIONAL on a cache layer existing — S10) | Any cache key is tenant-scoped (includes tenant_id/subdomain); acme and globex dashboards never share a cache entry. If stats are computed on demand, assert the query is tenant-filtered with no shared/global key. |
| 5 | Run the acme and globex reminder jobs concurrently | Neither leaks participants or notifications across tenants; results are partitioned strictly by tenant. |

## 6. Postconditions
- Cycle jobs, caches and notifications are strictly tenant-partitioned.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
