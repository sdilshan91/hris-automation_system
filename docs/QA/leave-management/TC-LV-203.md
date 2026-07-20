---
id: TC-LV-203
user_story: US-LV-010
module: Leave Management
priority: medium
type: functional
status: pass
created: 2026-06-14
---

# TC-LV-203: Tenant-configurable cancellation window -- allow cancellation up to N days before start (FR-7, AC-3)

> **✅ Conditional (N>0) arm now AUTOMATED (DF-20, 2026-07-20).** The policy is implemented — `Tenant.LeaveCancellationWindowDays`
> (0–90, org-profile) is read by `LeaveRequestService.CancelAsync`. Automated by `CancelLeaveRequestServiceTests`
> (`Cancel_ApprovedRequest_RespectsTenantConfiguredWindow`, `..._AtExactCutoffDay_IsBlocked_OneDayPast_IsAllowed`,
> `Cancel_PendingRequest_InsideTenantWindow_IsStillAllowed`) + `UpdateOrgProfileValidatorTests` (the server-side 0–90 bound)
> via `[Trait("TC","TC-LV-203")]`. The N=0 default remains covered by the existing cancel arms.

## 1. Test Objective
Verify the tenant-configurable policy that allows cancellation of an approved leave only up to N days before the start date (default N=0 = anytime before start). With a non-zero N, a cancellation submitted inside the cut-off window (fewer than N days before start) is blocked; outside the window it succeeds. **N>0 is now implemented (DF-20)** — an admin sets `LeaveCancellationWindowDays` via org-profile; `CancelAsync` blocks an Approved leave when `StartDate <= today + N`.

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
