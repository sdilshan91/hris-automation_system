---
id: TC-LV-256
user_story: US-LV-003
module: Leave Management
priority: high
type: functional
status: automated
created: 2026-07-04
---

# TC-LV-256: Apply-for-Leave loads its leave-type dropdown (BUG-102 regression)

## 1. Test Objective
Guard the fix for **BUG-102** (HIGH). The apply-leave screen loads leave types and
balances together via `forkJoin`. `LeaveRequestService.getMyBalances()` requested
`GET /api/v1/leaves/balances`, but the backend route is `GET /api/v1/leaves/my-balance`.
The 404 made the whole `forkJoin` error, so `leaveTypes` was never set and the leave-type
dropdown rendered empty — the employee could not apply for leave.

This test locks the **service URL contract** (the crisp, deterministic guard) and the
**component resilience** (a balances failure must not blank the dropdown).

## 2. Related Requirements
- User Story: US-LV-003 (Apply for Leave)
- Acceptance Criteria: AC-1 (submit a request — requires a populated type dropdown), AC-2 (per-type balance preview)
- Defect: BUG-102 (HIGH, Leave Management)
- Functional Requirement: FR-1 (apply form), FR-2 (real-time balance)

## 3. Preconditions
- Angular unit-test harness (`HttpClientTestingModule` / `provideHttpClientTesting`).
- No running backend required (contract is asserted at the HTTP-client boundary).

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Correct balances route | `/api/v1/leaves/my-balance` | Backend contract |
| Legacy (buggy) route | `/api/v1/leaves/balances` | Must NOT be requested |
| Leave types on load | 13 active types | Populates the dropdown |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Call `LeaveRequestService.getMyBalances()` under `HttpTestingController` | Exactly one `GET` is issued to a URL ending in `/leaves/my-balance`, with `withCredentials: true`. |
| 2 | Assert no request is made to the legacy `/leaves/balances` path | `expectNone('/api/v1/leaves/balances')` passes (the old path is gone). |
| 3 | Load `LeaveApplicationComponent` with `getLeaveTypes()` → 13 active types and `getMyBalances()` succeeding | `component.leaveTypes()` has 13 entries (dropdown non-empty); `isLoading()` is false. |
| 4 | Load the component with `getMyBalances()` erroring (balances endpoint down) | `component.leaveTypes()` is still populated — a balances failure degrades gracefully and does not blank the dropdown (`catchError` resilience). |

## 6. Postconditions
- The apply-leave dropdown is populated whenever leave types are available, independent of the balances call outcome.
- The service never targets the retired `/leaves/balances` route.

## 7. Test Category Tags
- [x] Happy path
- [x] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test

## 8. Automation Binding
- Runner: Karma + Jasmine (frontend unit).
- Service contract: `src/frontend/src/app/features/leave-management/services/leave-request.service.spec.ts`
  → `getMyBalances should GET /leaves/my-balance not /leaves/balances (BUG-102)`.
- Component resilience: `src/frontend/src/app/features/leave-management/components/leave-application/leave-application.component.spec.ts`
  → `BUG-102: dropdown still populates when getMyBalances errors`.
- Tag: `@TC-LV-256`.
