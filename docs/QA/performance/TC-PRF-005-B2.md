---
id: TC-PRF-005-B2
user_story: US-PRF-005
module: Performance Management
priority: medium
type: functional
status: draft
created: 2026-07-08
---

# TC-PRF-005-B2: 360 standalone reviewer-progress tracker endpoint — submitted vs pending counts per reviewer category (BUG-243 follow-up)

**Status:** DRAFT — BLOCKED: endpoint not yet implemented (BUG-243 follow-up)

> The Angular `feedback-360` service calls a standalone tracker endpoint, but this progress data currently only exists **embedded inside `.../360/.../results`** — there is no dedicated route to poll reviewer progress before results are ready. This stub documents the intended verification for the standalone tracker. See BUG-243 / BUG-244.

## 1. Test Objective
Verify a to-be-built `GET .../performance/360/cycles/{cycleId}/employees/{employeeId}/tracker` (or equivalent) that returns the 360 reviewer completion tracker for a cycle+employee **independent of the full results aggregation**: total assignments, submitted count, pending count, and a per-category breakdown (Self / Manager / Peer / Report), each reviewer's status, and (if anonymity permits) reviewer identity. Tenant-scoped; readable by HR (`Performance.Review.All`) or the direct manager (`Performance.Review.Team`).

## 2. Related Requirements
- User Story: US-PRF-005
- Acceptance Criteria: AC-B2 (360 standalone progress tracker)
- Functional Requirements: FR-3 (reviewer status tracking), AC-3 (reviewer marked Completed on submit)
- Defect: BUG-243 (parent; missing-endpoint half = BUG-244). Note: today the counts are only derivable from `.../results`, which is unavailable until aggregation.

## 3. Preconditions
- Endpoint implemented (removes this BLOCK).
- Tenant "acme" Active; a 360-enabled cycle with the target employee and 5 reviewer assignments (Self, Manager, 2 Peers, 1 Report); 2 have submitted, 3 pending.
- HR Officer (`Performance.Review.All`) authenticated in acme.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Assignments | 5 total | Self, Manager, Peer-A, Peer-B, Report-C |
| Submitted | 2 | e.g. Self + Peer-A |
| Pending | 3 | Manager, Peer-B, Report-C |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As HR, `GET .../360/cycles/{cycleId}/employees/{employeeId}/tracker` | 200; body reports total=5, submitted=2, pending=3. |
| 2 | Inspect per-category breakdown | Counts per category Self/Manager/Peer/Report and each reviewer's status (Submitted/Pending) are returned. |
| 3 | Submit one pending reviewer's feedback, re-fetch tracker | submitted=3, pending=2; the flipped reviewer now shows Submitted (tracker reflects live progress without needing the results aggregation). |
| 4 | As the direct manager (`Performance.Review.Team`), fetch tracker | 200; permitted (scoped to their report). |
| 5 | As an unrelated employee, fetch tracker | 403/404. |
| 6 | Confirm tenant scoping | Response contains only acme assignments; no cross-tenant leakage (covered structurally by the ISO suite). |

## 6. Postconditions
- No state change (read-only endpoint); the caller can poll reviewer progress at any point in the 360 window.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [x] Boundary test
- [x] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
