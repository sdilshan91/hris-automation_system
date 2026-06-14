---
id: TC-LV-158
user_story: US-LV-008
module: Leave Management
priority: high
type: functional
status: draft
created: 2026-06-14
---

# TC-LV-158: Monthly expiry job is a no-op before expiry and idempotent across runs (AC-3, FR-3, NFR-3)

## 1. Test Objective
Verify the scheduling and idempotency of `ProcessCarryForwardExpiryJob` (runs monthly, FR-3): it does nothing for carry-forward balances whose expiry date has not yet passed, expires them exactly once on/after expiry, and re-running it later does not create duplicate `expired` entries (AC-3, FR-3, NFR-3).

## 2. Related Requirements
- User Story: US-LV-008
- Acceptance Criteria: AC-3
- Functional Requirements: FR-3
- Non-Functional Requirements: NFR-3
- Business Rules: BR-3

## 3. Preconditions
- Tenant "acme"; employee "Sam" with 5 carry-forward Annual Leave days dated 2027-01-01, expiry 2027-03-31 (BR-3).
- The days remain fully unused through expiry.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Carry-forward days | 5 | -- |
| Expiry date | 2027-03-31 | -- |
| Job cadence | monthly | FR-3 |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Run the monthly job for Jan and Feb 2027 (pre-expiry) | No `expired` entry created -- carry-forward not yet expired (FR-3, BR-3). |
| 2 | Run the monthly job for Apr 2027 (first run on/after expiry) | One `expired` entry of -5 created for the unused carry-forward. |
| 3 | Run the monthly job again for May 2027 | No new `expired` entry -- the carry-forward was already expired; idempotent (NFR-3). |
| 4 | Verify balance steadiness | Sam's available balance reflects exactly one -5 expiry, regardless of how many times the job runs. |

## 6. Postconditions
- Carry-forward expires exactly once on/after its expiry date; repeated monthly runs are safe no-ops.

## 7. Test Category Tags
- [ ] Happy path
- [x] Negative test
- [x] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
