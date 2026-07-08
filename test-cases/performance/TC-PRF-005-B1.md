---
id: TC-PRF-005-B1
user_story: US-PRF-005
module: Performance Management
priority: high
type: integration
status: draft
created: 2026-07-08
---

# TC-PRF-005-B1: 360 reviewer set full-replace (PUT) — atomically replace all reviewer assignments for a cycle+employee, tenant-scoped, HR/manager authz (BUG-243 follow-up)

**Status:** DRAFT — BLOCKED: endpoint not yet implemented (BUG-243 follow-up)

> The Angular `feedback-360` service calls a **full-replace** save-reviewers endpoint that the backend does not expose — the Feedback360 controller only supports add-one / remove-one assignment. This stub documents the intended verification for when the `PUT` reviewer-set endpoint is built. See BUG-243 / BUG-244.

## 1. Test Objective
Verify a to-be-built `PUT .../performance/360/cycles/{cycleId}/employees/{employeeId}/reviewers` (full-replace) endpoint that accepts the complete desired reviewer set for a 360 cycle+employee and reconciles it **atomically**: reviewers no longer in the payload are removed, new reviewers are created, unchanged ones are preserved, all within a single transaction (partial failure rolls back). The write is tenant-scoped (server-derived `tenant_id`, foreign-tenant reviewer/employee references rejected) and authorized to HR (`Performance.Review.All`) or the direct manager (`Performance.Review.Team`) only. Each add/remove writes an audit row.

## 2. Related Requirements
- User Story: US-PRF-005
- Acceptance Criteria: AC-B1 (360 reviewer set full-replace PUT)
- Functional Requirements: FR-1, FR-2 (reviewer nomination for the 360 set)
- Defect: BUG-243 (parent — Performance FE↔BE contract broken; missing-endpoint half formalized as BUG-244)

## 3. Preconditions
- Endpoint implemented (removes this BLOCK).
- Tenant "acme" Active; a 360-enabled active cycle exists (US-PRF-004) with the target employee and an existing reviewer set (e.g. Self, Manager, 2 Peers).
- HR Officer (`Performance.Review.All`) and the target's direct manager (`Performance.Review.Team`) authenticated in acme.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Existing reviewers | Self, Manager, Peer-A, Peer-B | starting assignment set |
| Desired (PUT) set | Self, Manager, Peer-A, Report-C | Peer-B dropped, Report-C added |
| Foreign reference | reviewerId from tenant "beta" | must be rejected |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As HR, `PUT .../360/cycles/{cycleId}/employees/{employeeId}/reviewers` with the full desired set (Self, Manager, Peer-A, Report-C) | 200; response reflects the new set; Peer-B assignment removed, Report-C created, Self/Manager/Peer-A preserved (idempotent for unchanged rows). |
| 2 | Inspect persisted assignments | Exactly the PUT set exists for acme/cycle/employee; no orphaned Peer-B row; all rows `tenant_id`=acme. |
| 3 | Verify atomicity | Submit a payload containing one invalid reviewer among valid ones → whole request rejected (4xx), NO partial mutation persisted (old set intact). |
| 4 | As the direct manager (`Performance.Review.Team`), repeat a valid replace | 200; permitted. |
| 5 | As an employee lacking review permission, attempt the PUT | 403. |
| 6 | Include a `reviewerId` belonging to tenant "beta" | Rejected (404/400 foreign reference); server never assigns across tenants. |
| 7 | Inspect audit | An audit row per add and per remove (actor, cycle, employee, reviewer, action). |

## 6. Postconditions
- The reviewer set equals the last successful PUT payload; removed reviewers' assignments are gone; audit trail records each change; no cross-tenant assignment created.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
