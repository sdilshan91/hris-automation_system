---
id: TC-PRF-ISO-012
user_story: US-PRF-003
module: Performance Management
priority: high
type: security
status: draft
created: 2026-06-16
---

# TC-PRF-ISO-012: Team-review dashboard caches, manager-review notifications, and audit entries are tenant-scoped

## 1. Test Objective
Verify NFR-2 across the side-effect surfaces of US-PRF-003: any cache backing the Team Reviews dashboard / review-form is keyed per tenant (no shared/global key leaking another tenant's review state), the manager-review submission notification reaches only the owning tenant's employee (FR-7), and audit entries for rating actions are tenant-scoped (a globex user cannot read acme's review audit trail).

## 2. Related Requirements
- User Story: US-PRF-003
- Non-Functional Requirements: NFR-2
- Functional Requirements: FR-7

## 3. Preconditions
- acme and globex each have a manager with direct reports and an active cycle.
- Both tenants generate dashboard/list reads, submit reviews, and produce audit entries.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Cache key | includes tenant id | no shared/global key |
| Submission notice | acme employee | acme-only |
| Audit entries | acme review actions | acme-only readable |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Load the Team Reviews dashboard in acme, then in globex; inspect any cache key | Cache keys are tenant-scoped (include the tenant id); globex's dashboard never returns acme rows. CONDITIONAL: if the dashboard is computed on demand (no cache layer), assert the underlying queries are tenant-filtered with no shared/global key. |
| 2 | Submit an acme manager review; check who is notified | Only the acme employee (and acme actors) are notified; no globex user receives it (FR-7). Email delivery CONDITIONAL on the Notification System (S25) -- enqueue asserted. |
| 3 | Query the review audit log as a globex user | acme's rating/reopen audit entries are NOT visible (tenant-scoped). |
| 4 | Repeat the submission in globex | Only globex employees/actors are notified; acme receives nothing. |

## 6. Postconditions
- Dashboard caches, review notifications, and audit entries are strictly tenant-scoped; no cross-tenant leak.

## 7. Test Category Tags
- [ ] Happy path
- [ ] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
