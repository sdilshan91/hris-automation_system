---
id: TC-PRF-ISO-040
user_story: US-PRF-010
module: Performance Management
priority: high
type: security
status: blocked
created: 2026-06-16
---

# TC-PRF-ISO-040: Tenant-scoped downstream integration events (Core HR/Payroll/Training) + approval notifications + summary/budget caches + export artifacts (NFR-2)

## 1. Test Objective
Verify NFR-2 across the asynchronous + cross-module surfaces of recommendations: the downstream integration events emitted on approval (Core HR promotion update, Payroll one-time earning, Training enrollment) carry the originating tenant_id and are processed in that tenant's context; approval-workflow + recommendation notifications are addressed only to the owning tenant's users; any recommendation summary/budget cache is tenant-scoped (no shared/global key); and generated export artifacts (PDF/Excel) are tenant-scoped and never readable across tenants.

## 2. Related Requirements
- User Story: US-PRF-010
- Non-Functional Requirements: NFR-2
- Business Rules: BR-6 (downstream integration)
- Functional Requirements: FR-4 (approval notifications), FR-6 (export), FR-8 (budget)

## 3. Preconditions
- Tenants "acme" and "globex" each have approved recommendations driving downstream events, an active approval chain, and a recommendation summary/budget.
- A cache layer may or may not be present (cache assertions are CONDITIONAL on S10); the Notification System (S25) is available (enqueue asserted).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Tenant A | acme | approved promotion/bonus/training |
| Tenant B | globex | its own approvals |
| Surfaces | downstream events, notifications, caches, exports | tenant-scoped |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Approve an acme promotion/bonus/training recommendation; inspect the emitted downstream events | Each Core HR / Payroll / Training event carries acme's tenant_id and is enqueued/processed in acme's context (TC-PRF-010-10); it never targets globex employees/payroll/training. |
| 2 | Inspect approval + recommendation notifications | Notifications for acme's approval tasks / approved-rejected outcomes are addressed only to acme users; globex approvers/HR receive none of acme's. |
| 3 | Inspect the summary/budget cache (if a cache layer exists) | Any cached summary/budget aggregate is keyed by tenant (e.g. includes tenant_id + cycle + filter hash); acme and globex never share a cache entry. If computed on demand, the query is tenant-filtered with no shared/global key. |
| 4 | Generate an export (PDF/Excel) in acme | The artifact is stored/served under an acme-scoped path/identifier; a globex user cannot retrieve acme's export artifact by id/URL (404/403). |
| 5 | Run the same flows in globex | All downstream events, notifications, caches, and export artifacts are globex-scoped; zero acme leakage and vice versa. |

## 6. Postconditions
- Downstream integration events, approval/recommendation notifications, summary/budget caches, and export artifacts are all tenant-scoped; no cross-tenant processing, addressing, cache-sharing, or artifact retrieval occurs.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
