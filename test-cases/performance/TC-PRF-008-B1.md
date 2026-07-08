---
id: TC-PRF-008-B1
user_story: US-PRF-008
module: Performance Management
priority: medium
type: functional
status: draft
created: 2026-07-08
---

# TC-PRF-008-B1: PIP draft — save a Performance Improvement Plan as Draft before finalizing; editable later; distinct from the create-final path (BUG-243 follow-up)

**Status:** DRAFT — BLOCKED: endpoint not yet implemented (BUG-243 follow-up)

> The Angular `pip` service calls a `draft` action, but the backend exposes only the create-**final** PIP path — there is no save-as-draft route. This stub documents the intended verification. See BUG-243 / BUG-244.

## 1. Test Objective
Verify a to-be-built PIP draft endpoint (e.g. `POST .../performance/pip/draft` + a corresponding update) that persists a PIP in status **Draft** before it is finalized: the draft is saved (partial/relaxed validation vs. finalize), remains editable, is distinct from the create-final path (a draft does not notify the employee / start the PIP clock), and can later be finalized into an Active PIP. Tenant-scoped; authorized to the manager (`Performance.Review.Team`) / HR (`Performance.Review.All`) per US-PRF-008.

## 2. Related Requirements
- User Story: US-PRF-008
- Acceptance Criteria: AC-B1 (PIP save-as-draft, distinct from create-final)
- Functional Requirements: PIP authoring lifecycle (Draft → finalize → Active), authz manager/HR
- Defect: BUG-243 (parent; missing-endpoint half = BUG-244)

## 3. Preconditions
- Endpoint implemented (removes this BLOCK).
- Tenant "acme" Active; a manager (`Performance.Review.Team`) with a direct report authenticated in acme.
- A target employee eligible for a PIP.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| PIP target | direct report | eligible employee |
| Draft content | partial objectives/checkpoints | finalize-required fields may be incomplete |
| Final content | complete objectives, dates, checkpoints | required to finalize |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As the manager, `POST .../performance/pip/draft` with partial content | 201; PIP persisted with status **Draft**, `tenant_id`=acme; NO employee notification sent and the PIP clock is not started (distinct from create-final). |
| 2 | Re-open and update the draft (add objectives/checkpoints) | Update accepted; changes persist; status stays Draft. |
| 3 | Finalize the draft (the create-final / activate path) | With all required fields present → status transitions to Active; the finalize-time side effects fire (employee notified, checkpoints scheduled). |
| 4 | Attempt to finalize a draft missing required fields | Rejected 400 with the missing-field errors; status stays Draft (draft-time validation is relaxed, finalize-time is strict). |
| 5 | As an employee lacking PIP-authoring permission, POST a draft | 403. |
| 6 | Confirm tenant scoping | The draft is visible only within acme; other-tenant callers cannot read/edit it (ISO suite). |

## 6. Postconditions
- A Draft PIP exists, editable and side-effect-free, until explicitly finalized into an Active PIP; unauthorized authoring is blocked.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
