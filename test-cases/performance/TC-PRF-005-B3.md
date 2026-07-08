---
id: TC-PRF-005-B3
user_story: US-PRF-005
module: Performance Management
priority: high
type: security
status: draft
created: 2026-07-08
---

# TC-PRF-005-B3: 360 get-feedback-form by assignmentId — assigned reviewer fetches their form; non-assignee denied (BUG-243 follow-up)

**Status:** DRAFT — BLOCKED: endpoint not yet implemented (BUG-243 follow-up)

> The Angular `feedback-360` service fetches the feedback form a reviewer must fill **by `assignmentId`**, but the backend exposes no get-form-by-assignment route (only submit). This stub documents the intended verification. See BUG-243 / BUG-244.

## 1. Test Objective
Verify a to-be-built `GET .../performance/360/feedback/{assignmentId}/form` that returns the feedback form (competency questions + tenant rating scale + comment fields, plus reviewee/category context) for a specific 360 assignment, so the assigned reviewer can fill it. Authorization is the **assigned reviewer only** — a different user (even a valid acme reviewer on a different assignment) and any other-tenant caller are denied (403/404, no form leakage). Tenant-scoped.

## 2. Related Requirements
- User Story: US-PRF-005
- Acceptance Criteria: AC-B3 (get-feedback-form by assignmentId)
- Functional Requirements: FR-4 (competency-based feedback form + tenant rating scale)
- Defect: BUG-243 (parent; missing-endpoint half = BUG-244)

## 3. Preconditions
- Endpoint implemented (removes this BLOCK).
- Tenant "acme" Active; a 360 cycle with a Pending assignment `{assignmentId}` for reviewer "Ava Reyes" reviewing "Liam Carter".
- A second acme reviewer "Ben Cho" (assigned to a different assignment) and an unrelated employee authenticated for the negative cases.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Assignment | {assignmentId} | Ava → Liam, status Pending |
| Rating scale | 1-5 | tenant-configured |
| Non-assignee | Ben Cho | valid acme reviewer, different assignment |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As the assigned reviewer Ava, `GET .../360/feedback/{assignmentId}/form` | 200; body returns the competency questions, the tenant rating scale (1-5), per-competency comment fields, and reviewee/category context; no other reviewer's responses exposed. |
| 2 | As non-assignee Ben Cho, GET the same `{assignmentId}` form | 403 (or coded 404) — a reviewer cannot fetch another reviewer's assignment form. No form body returned. |
| 3 | As an employee with no 360 role, GET the form | 403/404. |
| 4 | As a reviewer of tenant "beta", GET the acme `{assignmentId}` | 404 (cross-tenant; assignment invisible — server-derived tenant scope). |
| 5 | GET a non-existent/garbage assignmentId | Coded 404; no enumeration signal (same shape as the cross-tenant 404). |

## 6. Postconditions
- No state change (read-only); only the assigned reviewer can retrieve the form; unauthorized and cross-tenant fetches leak nothing.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
