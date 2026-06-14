---
id: TC-LV-203
user_story: US-LV-010
module: Leave Management
priority: medium
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-203: Tenant-configurable cancellation window -- allow cancellation up to N days before start (CONDITIONAL; FR-7, AC-3)

## 1. Test Objective
Verify the tenant-configurable policy that allows cancellation of an approved leave only up to N days before the start date (default N=0 = anytime before start). With a non-zero N, a cancellation submitted inside the cut-off window (fewer than N days before start) is blocked; outside the window it succeeds. If tenant-policy config is not yet implemented, the default (N=0, anytime-before-start) is verified live and the N>0 window is recorded as conditional (FR-7).

## 2. Related Requirements
- User Story: US-LV-010
- Functional Requirements: FR-7
- Acceptance Criteria: AC-3
- Note: Tenant fiscal/policy config is CONDITIONAL on tenant-settings (calendar/default conventions reused module-wide per docs/vault/modules/leave-management.md). Default N=0 verified live.

## 3. Preconditions
- Tenant "acme"; today is 2026-06-14.
- Employee "Jane Smith" has an APPROVED future Annual Leave request R starting 2026-06-17 (3 days out).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| R start | 2026-06-17 | 3 days from today |
| Policy N (default) | 0 | anytime before start |
| Policy N (configured) | 5 | cut-off 5 days before start |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | (Live -- default policy N=0) Cancel R today (before its 2026-06-17 start) | Succeeds -- with the default policy, cancellation is allowed anytime before start (FR-7 default). |
| 2 | (CONDITIONAL -- policy config present) Set N=5 and attempt to cancel R (only 3 days before start, inside the 5-day cut-off) | Blocked with a policy-window message; status stays Approved. Mark CONDITIONAL on tenant-settings config. |
| 3 | (CONDITIONAL) With N=5, attempt to cancel a different request starting 10 days out (outside the cut-off) | Succeeds. |
| 4 | Verify default behavior is not spuriously restrictive | When no policy is configured, no N>0 window blocks an otherwise-eligible cancellation. |

## 6. Postconditions
- The default anytime-before-start window is verified live; the configurable N-day cut-off is verified by design and recorded CONDITIONAL on tenant-settings.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
