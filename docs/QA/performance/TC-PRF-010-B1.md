---
id: TC-PRF-010-B1
user_story: US-PRF-010
module: Performance Management
priority: medium
type: functional
status: draft
created: 2026-07-08
---

# TC-PRF-010-B1: Recommendation completed-cycles picker — GET cycles/completed for the recommendation workspace; only Completed cycles; tenant-scoped (BUG-243 follow-up)

**Status:** DRAFT — BLOCKED: endpoint not yet implemented (BUG-243 follow-up)

> The Angular `recommendation` service calls `cycles/completed` to populate the recommendation-workspace cycle picker, but the backend has **no completed-cycles list route**. This stub documents the intended verification. See BUG-243 / BUG-244.

## 1. Test Objective
Verify a to-be-built `GET .../performance/recommendations/cycles/completed` (or equivalent) that returns the tenant's **Completed** appraisal cycles for the recommendation workspace picker: only cycles in status Completed are returned (Active/Draft/Archived excluded per the built spec), tenant-scoped (acme's completed cycles only), authorized to the recommendation-workspace persona (HR/manager per US-PRF-010 authz).

## 2. Related Requirements
- User Story: US-PRF-010
- Acceptance Criteria: AC-B1 (recommendation completed-cycles picker)
- Functional Requirements: recommendation workspace requires a completed cycle to base recommendations on
- Defect: BUG-243 (parent; missing-endpoint half = BUG-244)

## 3. Preconditions
- Endpoint implemented (removes this BLOCK).
- Tenant "acme" Active with a mix of cycles: at least one Completed ("FY25 Annual"), one Active ("FY26-H1"), one Draft.
- A recommendation-workspace user (HR/manager per US-PRF-010) authenticated in acme.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| Completed cycle | FY25 Annual | should appear |
| Active cycle | FY26-H1 | should NOT appear |
| Draft cycle | FY26-H2 (draft) | should NOT appear |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | As the recommendation-workspace user, `GET .../recommendations/cycles/completed` | 200; list contains "FY25 Annual" (Completed) and excludes the Active and Draft cycles. |
| 2 | Complete an additional cycle, re-fetch | The newly-completed cycle now appears in the list. |
| 3 | With no completed cycles | 200 with an empty list (not a 500). |
| 4 | As a user lacking the recommendation-workspace permission | 403. |
| 5 | Confirm tenant scoping | An other-tenant user's completed-cycles list never contains acme's "FY25 Annual" (ISO suite). |

## 6. Postconditions
- Read-only; the picker shows only Completed cycles for the caller's tenant.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [x] Boundary test
- [x] Security test
- [x] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
